using System.Windows;
using Moonrise.Services;

namespace Moonrise;

public partial class MainWindow
{
    private CancellationTokenSource? _accountSyncCancellation;
    private async void LunarAccounts_Loaded(object sender, RoutedEventArgs e)
    {
        if (_visualQaMode || _accountSyncCancellation != null) return;
        _accountSyncCancellation = new();
        var token = _accountSyncCancellation.Token;
        Closed += (_, _) => _accountSyncCancellation.Cancel();
        var provider = new LunarAccountProvider(
            () => LauncherExecutableDetector.IsSupportedLauncher(_settings.LauncherExecutablePath) || new LauncherExecutableDetector().Detect().Count != 0);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var snapshot = await Task.Run(() => provider.RefreshAsync(token), token);
                // One dispatcher turn publishes the entire immutable projection.
                LunarAccountsList.ItemsSource = snapshot.Accounts;
                LunarAccountStatus.Text = snapshot.Diagnostic switch
                {
                    null => snapshot.Accounts.Count == 0 ? "В Lunar нет аккаунтов / No Lunar accounts" : "Аккаунты Lunar / Lunar accounts",
                    "LUNAR_NOT_INSTALLED" => "Lunar не найден / Lunar not found",
                    "ACCOUNT_STORE_MISSING" => "Хранилище аккаунтов отсутствует / Account store missing",
                    "ACCOUNT_STORE_UNREADABLE" => "Хранилище недоступно / Account store unreadable",
                    "ACCOUNT_STORE_INVALID" => "Некорректное хранилище / Invalid account store",
                    _ => "Ошибка синхронизации / Account sync failed"
                };
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) { }
        finally { _accountSyncCancellation.Dispose(); }
    }
}
