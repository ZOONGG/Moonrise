using System.Net;
using System.Net.Http;
using System.Globalization;
using Moonrise.Models;

namespace Moonrise.Services;

public sealed class SupportFlowController : IDisposable
{
    private readonly ISupportApiClient _apiClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<DateTimeOffset> _utcNow;
    private CancellationTokenSource? _flowCancellation;
    private SupportCheckoutSession? _session;
    private string? _lastAcceptedPaymentIntentId;
    private decimal? _pendingDirectAmount;
    private string? _pendingDirectLocale;
    private int _generation;

    public event EventHandler? StateChanged;
    public SupportFlowState State { get; private set; } = SupportFlowState.Closed;
    public IReadOnlyList<SupportMethod> Methods { get; private set; } = [];
    public SupportMethod? Method { get; private set; }
    public SupportCheckoutView? Checkout { get; private set; }
    public string? LastErrorCode { get; private set; }
    public Task? ActivePollingTask { get; private set; }
    public int StatusPollCount { get; private set; }

    public SupportFlowController(
        ISupportApiClient apiClient,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _apiClient = apiClient;
        _delayAsync = delayAsync ?? Task.Delay;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StopActiveCheckout(clearCheckout: true);
        SetState(SupportFlowState.LoadingMethods);
        try
        {
            var response = await _apiClient.GetMethodsAsync(cancellationToken);
            Methods = response.Methods.Where(IsSupportedMethod).ToArray();
            Method = Methods.FirstOrDefault(item => item.Enabled &&
                string.Equals(item.Id, "telegram_stars", StringComparison.OrdinalIgnoreCase))
                ?? Methods.FirstOrDefault(item => item.Enabled);
            if (Methods.Count == 0)
            {
                LastErrorCode = "METHOD_UNAVAILABLE";
                SetState(SupportFlowState.Failed);
                return;
            }
            LastErrorCode = null;
            SetState(SupportFlowState.ChoosingAmount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(SupportFlowState.Closed);
        }
        catch (SupportApiException exception)
        {
            LastErrorCode = exception.Code;
            SetState(SupportFlowState.Failed);
        }
        catch (HttpRequestException)
        {
            LastErrorCode = "NETWORK_UNAVAILABLE";
            SetState(SupportFlowState.Failed);
        }
    }

    public bool SelectMethod(string methodId)
    {
        var selected = Methods.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Id, methodId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            return false;
        StopActiveCheckout(clearCheckout: true);
        Method = selected;
        LastErrorCode = null;
        SetState(SupportFlowState.ChoosingAmount);
        return true;
    }

    public bool TryNormalizeAmount(
        string? customText,
        CultureInfo culture,
        out decimal amount) =>
        TryResolveAmountSelection(customText, culture, out amount, out _);

    public bool TryResolveAmountSelection(
        string? customText,
        CultureInfo culture,
        out decimal amount,
        out int? selectedPreset)
    {
        amount = 0;
        selectedPreset = null;
        if (Method is not { } method)
            return false;

        if (string.Equals(method.Id, "direct_crypto", StringComparison.OrdinalIgnoreCase))
        {
            if (!decimal.TryParse(customText, NumberStyles.Number, culture, out amount) ||
                amount < method.Custom.Min || decimal.Round(amount, 2) != amount)
                return false;
            foreach (var preset in method.Presets)
            {
                if (amount != preset)
                    continue;
                selectedPreset = preset;
                break;
            }
            return method.Custom.Enabled || selectedPreset is not null;
        }

        if (method.Custom.Max is not { } maximum ||
            !SupportAmountNormalizer.TryResolveSelection(
                customText,
                method.Custom.Min,
                maximum,
                method.Presets,
                culture,
                out var integerAmount,
                out selectedPreset))
            return false;
        amount = integerAmount;

        return method.Custom.Enabled || selectedPreset is not null;
    }

    public bool TryGetPresetInput(int preset, CultureInfo culture, out string input)
    {
        input = string.Empty;
        if (Method is not { } method || !method.Presets.Contains(preset))
            return false;

        input = Math.Clamp(preset, method.Custom.Min, method.Custom.Max ?? int.MaxValue).ToString(culture);
        return true;
    }

    public async Task BeginCheckoutAsync(
        decimal amount,
        string locale,
        CancellationToken cancellationToken = default,
        SupportAsset? asset = null)
    {
        if (Method is null)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var direct = string.Equals(Method.Id, "direct_crypto", StringComparison.OrdinalIgnoreCase);
        if (amount < Method.Custom.Min || decimal.Round(amount, direct ? 2 : 0) != amount ||
            (!direct && (Method.Custom.Max is not { } maximum || amount > maximum)))
            throw new ArgumentOutOfRangeException(nameof(amount));

        StopActiveCheckout(clearCheckout: true);
        if (direct && asset is null)
        {
            _pendingDirectAmount = amount;
            _pendingDirectLocale = locale;
            SetState(SupportFlowState.ChoosingAsset);
            return;
        }
        if (asset is not null &&
            (Method.Assets?.Any(item => item.Asset == asset.Asset && item.Network == asset.Network) != true))
            throw new ArgumentOutOfRangeException(nameof(asset));
        var generation = ++_generation;
        var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _flowCancellation = flowCancellation;
        StatusPollCount = 0;
        SetState(SupportFlowState.CreatingCheckout);
        try
        {
            var checkout = await _apiClient.CreateCheckoutAsync(
                Method.Id,
                new SupportCheckoutRequest(amount, true, "app", locale, asset),
                flowCancellation.Token);
            if (generation != _generation || flowCancellation.IsCancellationRequested)
                return;
            var checkoutAmount = direct ? checkout.UsdAmount : checkout.Amount;
            var checkoutCurrency = direct ? checkout.Asset : checkout.Currency;
            var checkoutPayload = direct ? checkout.PaymentUri ?? checkout.WalletAddress : checkout.CheckoutUrl;
            if (string.IsNullOrWhiteSpace(checkout.PaymentIntentId) ||
                string.Equals(checkout.PaymentIntentId, _lastAcceptedPaymentIntentId, StringComparison.Ordinal) ||
                !string.Equals(checkout.Provider, Method.Id, StringComparison.OrdinalIgnoreCase) ||
                checkoutAmount != amount ||
                string.IsNullOrWhiteSpace(checkoutCurrency) ||
                string.IsNullOrWhiteSpace(checkoutPayload) ||
                (direct && (checkout.Asset != asset?.Asset || checkout.Network != asset?.Network ||
                    string.IsNullOrWhiteSpace(checkout.CryptoAmount) ||
                    string.IsNullOrWhiteSpace(checkout.WalletAddress))))
            {
                LastErrorCode = "INVALID_RESPONSE";
                SetState(SupportFlowState.Failed);
                return;
            }
            if ((!direct && !CheckoutUrlValidator.TryValidateCheckout(Method.Id, checkoutPayload, out _)) ||
                (direct && checkout.PaymentUri is not null &&
                    !CheckoutUrlValidator.TryValidateDirectCrypto(checkout.PaymentUri, out _)))
            {
                LastErrorCode = "UNSAFE_CHECKOUT_URL";
                SetState(SupportFlowState.Failed);
                return;
            }
            if (string.IsNullOrWhiteSpace(checkout.StatusToken))
            {
                LastErrorCode = "INVALID_RESPONSE";
                SetState(SupportFlowState.Failed);
                return;
            }
            if (checkout.ExpiresAt <= _utcNow())
            {
                LastErrorCode = "PAYMENT_EXPIRED";
                SetState(SupportFlowState.Expired);
                return;
            }
            Checkout = new SupportCheckoutView(
                checkout.PaymentIntentId,
                checkout.Provider,
                checkoutCurrency,
                checkoutAmount!.Value,
                checkoutPayload,
                checkout.ExpiresAt,
                checkout.Asset,
                checkout.Network,
                checkout.NetworkName,
                checkout.CryptoAmount,
                checkout.WalletAddress,
                checkout.PaymentUri);
            _lastAcceptedPaymentIntentId = checkout.PaymentIntentId;
            var session = new SupportCheckoutSession(checkout.PaymentIntentId, checkout.StatusToken);
            _session = session;
            LastErrorCode = null;
            var pollingStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ActivePollingTask = PollAsync(session, generation, pollingStart.Task, flowCancellation.Token);
            SetState(SupportFlowState.Waiting);
            pollingStart.SetResult();
        }
        catch (OperationCanceledException) when (flowCancellation.IsCancellationRequested)
        {
        }
        catch (SupportApiException exception)
        {
            LastErrorCode = exception.Code;
            SetState(SupportFlowState.Failed);
        }
        catch (HttpRequestException)
        {
            LastErrorCode = "NETWORK_UNAVAILABLE";
            SetState(SupportFlowState.Failed);
        }
    }

    public Task BeginDirectCryptoCheckoutAsync(
        SupportAsset asset,
        CancellationToken cancellationToken = default)
    {
        if (_pendingDirectAmount is not { } amount || _pendingDirectLocale is not { } locale)
            throw new InvalidOperationException("Direct crypto amount has not been selected.");
        _pendingDirectAmount = null;
        _pendingDirectLocale = null;
        return BeginCheckoutAsync(amount, locale, cancellationToken, asset);
    }

    public void ChangeAmount()
    {
        StopActiveCheckout(clearCheckout: true);
        LastErrorCode = null;
        _pendingDirectAmount = null;
        _pendingDirectLocale = null;
        SetState(SupportFlowState.ChoosingAmount);
    }

    public bool TryGetActiveCheckout(out SupportCheckoutView? checkout)
    {
        checkout = Checkout;
        if (checkout is null || State is not (SupportFlowState.Waiting or SupportFlowState.ConnectivityIssue))
        {
            checkout = null;
            return false;
        }
        if (checkout.ExpiresAt > _utcNow())
            return true;

        StopActiveCheckout(clearCheckout: true);
        LastErrorCode = "PAYMENT_EXPIRED";
        SetState(SupportFlowState.Expired);
        checkout = null;
        return false;
    }

    public void Close()
    {
        StopActiveCheckout(clearCheckout: true);
        Method = null;
        Methods = [];
        _pendingDirectAmount = null;
        _pendingDirectLocale = null;
        LastErrorCode = null;
        SetState(SupportFlowState.Closed);
    }

    private async Task PollAsync(
        SupportCheckoutSession session,
        int generation,
        Task pollingStart,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.Zero;
        var failureCount = 0;
        try
        {
            // Publish both the waiting state and ActivePollingTask before the immediate first
            // request so a fast paid response cannot be overwritten by the setup path.
            await pollingStart;
            while (IsCurrent(session, generation, cancellationToken))
            {
                if (Checkout is not { } activeCheckout || activeCheckout.ExpiresAt <= _utcNow())
                {
                    TransitionToExpired(session);
                    return;
                }
                if (delay > TimeSpan.Zero)
                {
                    var untilExpiry = activeCheckout.ExpiresAt - _utcNow();
                    if (untilExpiry <= TimeSpan.Zero)
                    {
                        TransitionToExpired(session);
                        return;
                    }
                    await _delayAsync(delay < untilExpiry ? delay : untilExpiry, cancellationToken);
                }
                if (!IsCurrent(session, generation, cancellationToken))
                    return;
                if (Checkout is not { } checkoutBeforePoll || checkoutBeforePoll.ExpiresAt <= _utcNow())
                {
                    TransitionToExpired(session);
                    return;
                }

                SupportStatusResult result;
                try
                {
                    StatusPollCount++;
                    result = await session.GetStatusAsync(_apiClient, cancellationToken);
                    failureCount = 0;
                }
                catch (SupportApiException exception) when (IsTransientStatusFailure(exception))
                {
                    if (!IsCurrent(session, generation, cancellationToken))
                        return;
                    LastErrorCode = exception.Code;
                    SetState(SupportFlowState.ConnectivityIssue);
                    failureCount++;
                    delay = exception.RetryAfter ?? Backoff(failureCount);
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // HttpClient reports request timeouts as TaskCanceledException. A timeout is
                    // transient and must not silently terminate payment polling.
                    LastErrorCode = "NETWORK_TIMEOUT";
                    SetStateIfCurrent(SupportFlowState.ConnectivityIssue, session, generation);
                    delay = Backoff(++failureCount);
                    continue;
                }
                catch (HttpRequestException)
                {
                    LastErrorCode = "NETWORK_UNAVAILABLE";
                    SetStateIfCurrent(SupportFlowState.ConnectivityIssue, session, generation);
                    delay = Backoff(++failureCount);
                    continue;
                }
                catch (SupportApiException exception) when (
                    exception.Code == "PAYMENT_NOT_FOUND" || exception.StatusCode == HttpStatusCode.NotFound)
                {
                    if (!IsCurrent(session, generation, cancellationToken))
                        return;
                    LastErrorCode = exception.Code;
                    TransitionToExpired(session);
                    return;
                }
                catch (SupportApiException exception)
                {
                    if (!IsCurrent(session, generation, cancellationToken))
                        return;
                    LastErrorCode = exception.Code;
                    CompleteSession(session, clearCheckout: true);
                    SetState(SupportFlowState.Failed);
                    return;
                }

                if (!IsCurrent(session, generation, cancellationToken))
                    return;
                if (!session.Matches(result.Payment) ||
                    Checkout is not { } currentCheckout ||
                    result.Payment.Amount != currentCheckout.Amount ||
                    !string.Equals(result.Payment.Provider, currentCheckout.Provider, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(result.Payment.Currency, currentCheckout.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    LastErrorCode = "INVALID_CHECKOUT_SESSION";
                    TransitionToExpired(session);
                    return;
                }
                LastErrorCode = null;
                delay = result.RetryAfter;
                switch (result.Payment.Status.Trim().ToLowerInvariant())
                {
                    case "pending":
                        SetState(SupportFlowState.Waiting);
                        break;
                    case "paid":
                        CompleteSession(session, clearCheckout: false);
                        SetState(SupportFlowState.Paid);
                        return;
                    case "expired":
                        LastErrorCode = "PAYMENT_EXPIRED";
                        TransitionToExpired(session);
                        return;
                    case "refunded":
                        CompleteSession(session, clearCheckout: true);
                        SetState(SupportFlowState.Refunded);
                        return;
                    default:
                        LastErrorCode = "PAYMENT_FAILED";
                        CompleteSession(session, clearCheckout: true);
                        SetState(SupportFlowState.Failed);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or NotSupportedException)
        {
            if (!IsCurrent(session, generation, cancellationToken))
                return;
            LastErrorCode = "POLLING_FAILED";
            CompleteSession(session, clearCheckout: true);
            SetState(SupportFlowState.Failed);
        }
    }

    private void StopActiveCheckout(bool clearCheckout)
    {
        _generation++;
        var flowCancellation = _flowCancellation;
        var session = _session;
        _flowCancellation = null;
        _session = null;
        ActivePollingTask = null;
        flowCancellation?.Cancel();
        session?.Dispose();
        flowCancellation?.Dispose();
        if (clearCheckout)
            Checkout = null;
    }

    private bool IsCurrent(
        SupportCheckoutSession session,
        int generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        generation == _generation &&
        ReferenceEquals(_session, session);

    private void SetStateIfCurrent(
        SupportFlowState state,
        SupportCheckoutSession session,
        int generation)
    {
        if (generation == _generation && ReferenceEquals(_session, session))
            SetState(state);
    }

    private void CompleteSession(SupportCheckoutSession session, bool clearCheckout)
    {
        if (!ReferenceEquals(_session, session))
            return;
        _session = null;
        session.Dispose();
        _flowCancellation?.Dispose();
        _flowCancellation = null;
        if (clearCheckout)
            Checkout = null;
    }

    private void TransitionToExpired(SupportCheckoutSession session)
    {
        CompleteSession(session, clearCheckout: true);
        SetState(SupportFlowState.Expired);
    }

    private static TimeSpan Backoff(int failureCount) =>
        TimeSpan.FromSeconds(Math.Min(15, 2 * Math.Pow(2, Math.Min(failureCount - 1, 3))));

    private static bool IsTransientStatusFailure(SupportApiException exception) =>
        exception.Code == "INVALID_RESPONSE" ||
        exception.StatusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool IsSupportedMethod(SupportMethod method) =>
        method.Id.ToLowerInvariant() switch
        {
            "telegram_stars" => string.Equals(method.Currency, "XTR", StringComparison.OrdinalIgnoreCase) &&
                method.Custom.Max is { } maximum && maximum >= method.Custom.Min,
            "direct_crypto" => string.Equals(method.Currency, "USD", StringComparison.OrdinalIgnoreCase) && method.Assets?.Count > 0,
            _ => false
        } && method.Custom.Min > 0;

    private void SetState(SupportFlowState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Close();

    private sealed class SupportCheckoutSession(string paymentIntentId, string statusToken) : IDisposable
    {
        private string? _statusToken = statusToken;

        public Task<SupportStatusResult> GetStatusAsync(ISupportApiClient client, CancellationToken cancellationToken)
        {
            var token = _statusToken ?? throw new ObjectDisposedException(nameof(SupportCheckoutSession));
            return client.GetPaymentStatusAsync(paymentIntentId, token, cancellationToken);
        }

        public bool Matches(PublicPaymentStatus payment) =>
            string.Equals(payment.PaymentIntentId, paymentIntentId, StringComparison.Ordinal);

        public void Dispose() => _statusToken = null;
    }
}
