using CommunityToolkit.Mvvm.ComponentModel;
using Wattzicht.App.Services;

namespace Wattzicht.App.ViewModels;

/// <summary>
/// Gemeenschappelijke basis: laadstatus, foutmelding en nette afhandeling van API-fouten.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _title = string.Empty;

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnErrorChanged();

    /// <summary>Hook voor afgeleide ViewModels die op een (nieuwe) foutmelding willen reageren.</summary>
    protected virtual void OnErrorChanged() { }

    /// <summary>
    /// Voert een actie uit met IsBusy en zet een foutmelding bij een ApiException.
    /// Bij 401 wordt de gebruiker uitgelogd via <paramref name="auth"/>.
    /// </summary>
    protected async Task RunSafeAsync(Func<Task> action, AuthService? auth = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            if (ex.IsUnauthorized && auth is not null)
                await auth.LogoutAsync();
        }
        catch (OperationCanceledException)
        {
            // Genegeerd: gebruiker verliet het scherm.
        }
        catch (Exception)
        {
            ErrorMessage = Taal.T("algemeen.er-ging-iets-mis");
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected static Task ShowAlertAsync(string title, string message, string? cancel = null) =>
        Shell.Current?.CurrentPage?.DisplayAlert(title, message, cancel ?? Taal.T("algemeen.oke"))
        ?? Task.CompletedTask;

    protected static Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel = null) =>
        Shell.Current?.CurrentPage?.DisplayAlert(title, message, accept, cancel ?? Taal.T("algemeen.annuleren"))
        ?? Task.FromResult(false);
}
