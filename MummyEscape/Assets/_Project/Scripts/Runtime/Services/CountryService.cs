using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace MummyEscape.Services
{
    /// <summary>Player country for the per-country ranking: device region by default, overridable in the profile.</summary>
    public static class CountryService
    {
        public readonly struct Country
        {
            public readonly string Code;
            public readonly string Name;
            public Country(string code, string name) { Code = code; Name = name; }
        }

        /// <summary>Countries offered in the picker, sorted by French name.</summary>
        public static readonly Country[] All =
        {
            new Country("ZA", "Afrique du Sud"), new Country("DZ", "Algérie"), new Country("DE", "Allemagne"),
            new Country("SA", "Arabie saoudite"), new Country("AR", "Argentine"), new Country("AU", "Australie"),
            new Country("AT", "Autriche"), new Country("BE", "Belgique"), new Country("BJ", "Bénin"),
            new Country("BR", "Brésil"), new Country("BF", "Burkina Faso"), new Country("CM", "Cameroun"),
            new Country("CA", "Canada"), new Country("CL", "Chili"), new Country("CN", "Chine"),
            new Country("CO", "Colombie"), new Country("KR", "Corée du Sud"), new Country("CI", "Côte d'Ivoire"),
            new Country("DK", "Danemark"), new Country("EG", "Égypte"), new Country("AE", "Émirats arabes unis"),
            new Country("ES", "Espagne"), new Country("US", "États-Unis"), new Country("FI", "Finlande"),
            new Country("FR", "France"), new Country("GA", "Gabon"), new Country("GR", "Grèce"),
            new Country("HT", "Haïti"), new Country("HU", "Hongrie"), new Country("IN", "Inde"),
            new Country("ID", "Indonésie"), new Country("IE", "Irlande"), new Country("IL", "Israël"),
            new Country("IT", "Italie"), new Country("JP", "Japon"), new Country("LB", "Liban"),
            new Country("LU", "Luxembourg"), new Country("MG", "Madagascar"), new Country("ML", "Mali"),
            new Country("MA", "Maroc"), new Country("MX", "Mexique"), new Country("MC", "Monaco"),
            new Country("NO", "Norvège"), new Country("NZ", "Nouvelle-Zélande"), new Country("NL", "Pays-Bas"),
            new Country("PE", "Pérou"), new Country("PH", "Philippines"), new Country("PL", "Pologne"),
            new Country("PT", "Portugal"), new Country("CD", "RD Congo"), new Country("RO", "Roumanie"),
            new Country("GB", "Royaume-Uni"), new Country("SN", "Sénégal"), new Country("SE", "Suède"),
            new Country("CH", "Suisse"), new Country("TH", "Thaïlande"), new Country("TN", "Tunisie"),
            new Country("TR", "Turquie"), new Country("UA", "Ukraine"), new Country("VN", "Viêt Nam"),
        };

        public static string NameOf(string code)
        {
            foreach (var c in All) if (c.Code == code) return Loc.T(c.Name);
            return string.IsNullOrEmpty(code) ? Loc.T("Inconnu") : code;
        }

        /// <summary>Countries sorted by their name in the current game language.</summary>
        public static Country[] Sorted()
        {
            var list = Array.ConvertAll(All, c => new Country(c.Code, Loc.T(c.Name)));
            System.Globalization.CultureInfo culture;
            try { culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Info.Code); }
            catch (Exception) { culture = System.Globalization.CultureInfo.InvariantCulture; }
            Array.Sort(list, (a, b) => string.Compare(a.Name, b.Name, culture, System.Globalization.CompareOptions.IgnoreNonSpace));
            return list;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string _MummyCountryCode();
#endif

        /// <summary>
        /// Countries where a paid random draw counts as gambling (Belgium: loot boxes are banned). Scarabs can be bought
        /// with golden scarabs, so their wheel stays hidden there; the seal wheel (seals are only won) stays.
        /// </summary>
        static readonly string[] NoLootBoxes = { "BE" };

        /// <summary>True when the profile country, the device region or the SIM card says the player is in such a country.</summary>
        public static bool LootBoxesBanned(string profileCountry) =>
            Array.IndexOf(NoLootBoxes, (profileCountry ?? "").ToUpperInvariant()) >= 0
            || Array.IndexOf(NoLootBoxes, Detect()) >= 0
            || Array.IndexOf(NoLootBoxes, SimCountry()) >= 0;

        static string _sim;

        /// <summary>Country of the SIM card (or of the mobile network), "" without one.</summary>
        public static string SimCountry()
        {
            if (_sim != null) return _sim;
            string code = "";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var phone = activity.Call<AndroidJavaObject>("getSystemService", "phone"))
                {
                    code = phone?.Call<string>("getSimCountryIso");
                    if (string.IsNullOrEmpty(code)) code = phone?.Call<string>("getNetworkCountryIso");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Country] SIM country unreadable: " + e.Message);
            }
#endif
            code = (code ?? "").Trim().ToUpperInvariant();
            _sim = code.Length == 2 ? code : "";
            return _sim;
        }

        static string _detected;

        /// <summary>Device region as ISO 3166 alpha-2 ("FR"), "" when it cannot be read.</summary>
        public static string Detect()
        {
            if (_detected != null) return _detected;
            string code = "";
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (var current = locale.CallStatic<AndroidJavaObject>("getDefault"))
                    code = current.Call<string>("getCountry");
#elif UNITY_IOS && !UNITY_EDITOR
                code = _MummyCountryCode();
#else
                code = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Country] detection failed: " + e.Message);
            }
            code = (code ?? "").Trim().ToUpperInvariant();
            _detected = code.Length == 2 ? code : "";
            return _detected;
        }
    }
}
