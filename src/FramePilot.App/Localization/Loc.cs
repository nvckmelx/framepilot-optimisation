using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace FramePilot.App.Localization;

/// <summary>Runtime-switchable RU/EN strings. XAML binds through {l:T Key}; code uses Loc.T("Key").</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _language = "ru";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string Language
    {
        get => _language;
        set
        {
            var v = value == "en" ? "en" : "ru";
            if (v == _language) return;
            _language = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsRussian => _language == "ru";

    public string this[string key] => Strings.Get(_language, key);

    public static string T(string key) => Instance[key];
    public static string F(string key, params object[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Instance[key], args);
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
