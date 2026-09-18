using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace Moonrise;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\Moonrise.SingleInstance";
    private const string ActivationPipeName = "Moonrise.Activation.v1";
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationTask;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var visualQaMode = string.Equals(
            Environment.GetEnvironmentVariable("MOONRISE_VISUAL_QA"),
            "1",
            StringComparison.Ordinal);
        var activationArgument = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("moonrise:", StringComparison.OrdinalIgnoreCase));
        if (!visualQaMode)
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out var firstInstance);
            if (!firstInstance)
            {
                SendActivation(activationArgument);
                Shutdown();
                return;
            }

            _ownsMutex = true;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var window = new MainWindow(activationArgument, visualQaMode);
        MainWindow = window;
        window.Show();

        if (visualQaMode) return;

        _activationCancellation = new CancellationTokenSource();
        _activationTask = ListenForActivationsAsync(_activationCancellation.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        try
        {
            _activationTask?.Wait(500);
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(item => item is OperationCanceledException))
        {
        }

        _activationCancellation?.Dispose();
        if (_ownsMutex)
            _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async Task ListenForActivationsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var argument = await reader.ReadLineAsync(cancellationToken);
                if (argument is { Length: > 2048 })
                    argument = null;
                await Dispatcher.InvokeAsync(
                    () => (MainWindow as MainWindow)?.HandleExternalActivation(argument),
                    System.Windows.Threading.DispatcherPriority.Normal,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private static void SendActivation(string? argument)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                ActivationPipeName,
                PipeDirection.Out,
                PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            writer.WriteLine(argument ?? string.Empty);
        }
        catch (IOException)
        {
        }
        catch (TimeoutException)
        {
        }
    }
}
