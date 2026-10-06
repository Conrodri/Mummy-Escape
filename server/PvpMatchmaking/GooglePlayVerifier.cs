// Mummy Rush — vérification des achats auprès de Google Play (Google Play Developer API, purchases.products).
// Le compte de service Google (sa clé JSON) est rangé dans le Secret Manager d'Unity Gaming Services, jamais dans le jeu.
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MummyEscape.Pvp;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace MummyEscape.Pvp.Server
{
    public sealed class GooglePlayVerifier : IPurchaseVerifier
    {
        public const string PackageName = "com.mummyrush.game";
        /// <summary>Secret : le fichier JSON de la clé du compte de service Google (rôle « Afficher les données financières, commandes »).</summary>
        public const string ServiceAccountSecret = "GOOGLE_PLAY_SERVICE_ACCOUNT";
        const string Scope = "https://www.googleapis.com/auth/androidpublisher";

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        static string _accessToken;
        static DateTime _accessTokenExpiresUtc;

        readonly IGameApiClient _api;
        readonly IExecutionContext _ctx;
        readonly ILogger _logger;

        public GooglePlayVerifier(IGameApiClient api, IExecutionContext ctx, ILogger logger)
        {
            _api = api;
            _ctx = ctx;
            _logger = logger;
        }

        static string Url(string productId, string token) =>
            "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/" + PackageName + "/purchases/products/" +
            Uri.EscapeDataString(productId) + "/tokens/" + Uri.EscapeDataString(token);

        public async Task<PurchaseCheck> VerifyAsync(string productId, string purchaseToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Url(productId, purchaseToken));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync());
                var response = await Http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Achat refusé par Google ({code}) : {body}", (int)response.StatusCode, body);
                    return new PurchaseCheck { Error = "NOT_VERIFIED" };
                }
                var purchase = JObject.Parse(body);
                // 0 : acheté ; 1 : annulé ; 2 : en attente (paiement différé, crédité quand il aboutit).
                int state = purchase.Value<int?>("purchaseState") ?? -1;
                if (state == 2) return new PurchaseCheck { Error = "PENDING" };
                if (state != 0) return new PurchaseCheck { Error = "NOT_VERIFIED" };
                string orderId = purchase.Value<string>("orderId");
                return new PurchaseCheck { Ok = true, OrderId = string.IsNullOrEmpty(orderId) ? "token-" + Hash(purchaseToken) : orderId };
            }
            catch (Exception e)
            {
                _logger.LogError("Vérification d'achat impossible : {msg}", e.Message);
                return new PurchaseCheck { Error = "STORE_UNAVAILABLE" };
            }
        }

        public async Task ConsumeAsync(string productId, string purchaseToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, Url(productId, purchaseToken) + ":consume");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync());
                var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Achat non consommé ({code}) : {body}", (int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (Exception e)
            {
                // Déjà crédité (registre des commandes) : le prochain appel du jeu pour cet achat le consommera.
                _logger.LogWarning("Achat non consommé : {msg}", e.Message);
            }
        }

        static string Hash(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 32).ToLowerInvariant();
        }

        /// <summary>Un jeton OAuth du compte de service (JWT signé RS256 échangé chez Google), gardé jusqu'à peu avant son expiration.</summary>
        async Task<string> AccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresUtc) return _accessToken;
            var secret = await _api.SecretManager.GetSecret(_ctx, ServiceAccountSecret);
            var key = JObject.Parse(secret.Value);
            string email = key.Value<string>("client_email");
            string tokenUri = key.Value<string>("token_uri") ?? "https://oauth2.googleapis.com/token";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            string claims = Base64Url(Encoding.UTF8.GetBytes(new JObject
            {
                ["iss"] = email, ["scope"] = Scope, ["aud"] = tokenUri, ["iat"] = now, ["exp"] = now + 3600,
            }.ToString(Newtonsoft.Json.Formatting.None)));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.Value<string>("private_key"));
            string signature = Base64Url(rsa.SignData(Encoding.UTF8.GetBytes(header + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var response = await Http.PostAsync(tokenUri, new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new System.Collections.Generic.KeyValuePair<string, string>("assertion", header + "." + claims + "." + signature),
            }));
            var token = JObject.Parse(await response.Content.ReadAsStringAsync());
            _accessToken = token.Value<string>("access_token") ?? throw new InvalidOperationException("Jeton Google refusé");
            _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds((token.Value<int?>("expires_in") ?? 3600) - 120);
            return _accessToken;
        }

        static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
