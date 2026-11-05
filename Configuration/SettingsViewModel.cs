using Newtonsoft.Json;

namespace Autodraw.Configuration;

/// <summary>Editable configuration draft used by the Settings window.</summary>
public sealed class SettingsViewModel
{
    public AutoDrawConfig Draft { get; private set; } = DdConfigService.Current;
    public string Json { get; private set; } = string.Empty;
    public SettingsViewModel() => RefreshJson();
    public void RefreshJson() => Json = JsonConvert.SerializeObject(Draft, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include });
    public bool TryReplaceJson(string json, out string error)
    {
        error = string.Empty;
        try
        {
            var value = JsonConvert.DeserializeObject<AutoDrawConfig>(json);
            if (value is null) { error = "The file contains no configuration."; return false; }
            if (!DdConfigService.Validate(value, out error)) return false;
            Draft = value; Json = json; return true;
        }
        catch (System.Exception ex) { error = ex.Message; return false; }
    }
    public bool Apply(out string error) => DdConfigService.TryApply(Draft, out error);
}
