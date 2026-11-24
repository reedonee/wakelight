using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Wakelight
{
    /// <summary>Current weather from Open-Meteo (free, no API key). Only the city name you type is sent.</summary>
    public static class Weather
    {
        public class Report
        {
            public string Place;
            public double Temperature;
            public int Code;
            public bool IsDay;
            public DateTime Fetched;
            public string Glyph { get { return GlyphFor(Code, IsDay); } }
            public string Summary { get { return SummaryFor(Code); } }
        }

        public static Report Current { get; private set; }
        public static event Action Updated;
        static string lastQuery;
        static bool busy;

        public static void Refresh(Settings s, bool force)
        {
            if (!s.ShowWeather || string.IsNullOrWhiteSpace(s.WeatherCity) || busy) return;
            string key = s.WeatherCity.Trim().ToLowerInvariant() + (s.WeatherFahrenheit ? "|f" : "|c");
            if (!force && key == lastQuery && Current != null && (DateTime.Now - Current.Fetched).TotalMinutes < 30) return;
            busy = true;
            string city = s.WeatherCity.Trim();
            bool fahrenheit = s.WeatherFahrenheit;
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            Task.Run(() =>
            {
                try
                {
                    var r = Fetch(city, fahrenheit);
                    ui.BeginInvoke(new Action(() =>
                    {
                        lastQuery = key; Current = r; busy = false;
                        Log.Write(r == null ? "weather: city not found" : "weather: " + r.Place + " " + r.Temperature.ToString("0") + "°");
                        if (Updated != null) Updated();
                    }));
                }
                catch (Exception ex)
                {
                    ui.BeginInvoke(new Action(() => { busy = false; Log.Write("weather failed: " + ex.Message); }));
                }
            });
        }

        static Report Fetch(string city, bool fahrenheit)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Encoding = System.Text.Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Wakelight/" + Program.Version;
                string geo = wc.DownloadString("https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&name=" + Uri.EscapeDataString(city));
                var lat = Regex.Match(geo, "\"latitude\"\\s*:\\s*(-?[0-9.]+)");
                var lon = Regex.Match(geo, "\"longitude\"\\s*:\\s*(-?[0-9.]+)");
                var name = Regex.Match(geo, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                if (!lat.Success || !lon.Success) return null;

                string url = "https://api.open-meteo.com/v1/forecast?current=temperature_2m,weather_code,is_day&latitude=" +
                             lat.Groups[1].Value + "&longitude=" + lon.Groups[1].Value + (fahrenheit ? "&temperature_unit=fahrenheit" : "");
                string wx = wc.DownloadString(url);
                int cur = wx.IndexOf("\"current\"", StringComparison.Ordinal);
                if (cur < 0) return null;
                string c = wx.Substring(cur);
                var t = Regex.Match(c, "\"temperature_2m\"\\s*:\\s*(-?[0-9.]+)");
                var code = Regex.Match(c, "\"weather_code\"\\s*:\\s*([0-9]+)");
                var day = Regex.Match(c, "\"is_day\"\\s*:\\s*([0-9])");
                if (!t.Success) return null;
                return new Report
                {
                    Place = name.Success ? Regex.Unescape(name.Groups[1].Value) : city,
                    Temperature = double.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture),
                    Code = code.Success ? int.Parse(code.Groups[1].Value) : 0,
                    IsDay = !day.Success || day.Groups[1].Value == "1",
                    Fetched = DateTime.Now
                };
            }
        }

        /// <summary>Glyph from Segoe UI Symbol (ships with every Windows 10/11).</summary>
        public const string GlyphFont = "Segoe UI Symbol";

        static string GlyphFor(int code, bool day)
        {
            if (code == 0) return day ? "☀" : "☾";           // sun / moon
            if (code <= 2) return day ? "⛅" : "☁";           // sun behind cloud / cloud
            if (code == 3 || code == 45 || code == 48) return "☁"; // cloud (fog)
            if (code >= 51 && code <= 67) return "☂";             // umbrella (rain)
            if (code >= 71 && code <= 77) return "❄";             // snowflake
            if (code >= 80 && code <= 82) return "☔";             // umbrella with rain
            if (code >= 85 && code <= 86) return "❄";
            if (code >= 95) return "⛈";                           // thunder cloud
            return "☁";
        }

        static string SummaryFor(int code)
        {
            if (code == 0) return "Clear";
            if (code <= 2) return "Partly cloudy";
            if (code == 3) return "Cloudy";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 67) return "Rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Showers";
            if (code >= 85 && code <= 86) return "Snow showers";
            if (code >= 95) return "Thunderstorm";
            return "";
        }
    }
}
