using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Tb16p.Ui.Localization;

/// <summary>
/// Current UI language. XAML binds to the indexer (through <see cref="TrExtension"/>), so
/// raising "Item[]" re-reads every string and the language switches without a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _language = "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed, for text built in code.</summary>
    public event Action? LanguageChanged;

    public string Language
    {
        get => _language;
        set
        {
            var language = value == "vi" ? "vi" : "en";
            if (language == _language)
                return;
            _language = language;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke();
        }
    }

    public string this[string key] => Strings.Get(key, _language == "vi");

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Instance[key], args);
}

/// <summary><c>{loc:Tr Key}</c>: a live-updating localized string.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
