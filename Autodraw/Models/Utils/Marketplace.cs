using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Autodraw.Models.Utils;

public abstract class Marketplace
{
    public static string API = "https://auto-draw.com/api/";
    private static readonly HttpClient client = new HttpClient();
    
    public async static Task<JArray> List(string type) // 'type' can be either "theme" or "config"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}list?page=1&filter={type}");
        if (!response.IsSuccessStatusCode) return null!;
        var JSONResponse = await response.Content.ReadAsStringAsync();
        dynamic Response = (JObject)JsonConvert.DeserializeObject(JSONResponse);
        return Response.items;
    }
    
    public async static Task<String> Download(int id) // filename should be in the format of "{theme name}.{file extension}"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}download?id={id}");
        if (!response.IsSuccessStatusCode) return null!;
        var FileResponse = await response.Content.ReadAsStringAsync();
        File.WriteAllText(Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"')), FileResponse);
        return Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"'));
    }
}