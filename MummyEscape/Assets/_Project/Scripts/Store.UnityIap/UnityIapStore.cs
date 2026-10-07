using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MummyEscape.Pvp;
using UnityEngine;
using UnityEngine.Purchasing;

namespace MummyEscape.Monetization
{
    /// <summary>
    /// Google Play Billing through Unity IAP 5: the golden scarab packs (consumables). A paid order stays pending until the
    /// server has checked it with Google and credited it (<see cref="Finish"/>): the server consumes it, the store then
    /// acknowledges it. An order the game could not finish comes back on the next start (<see cref="Unfinished"/>).
    /// Only on Android devices: the editor keeps the simulated store.
    /// </summary>
    public sealed class UnityIapStore : IStoreProvider
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Store.Provider = new UnityIapStore();
#endif

        readonly StoreController _store;
        bool _fetched;
        TaskCompletionSource<PurchaseOutcome> _buying;
        string _buyingProduct;
        // Paid, not finished yet: by purchase token.
        readonly Dictionary<string, (string product, PendingOrder order)> _pending = new Dictionary<string, (string, PendingOrder)>();

        UnityIapStore()
        {
            _store = UnityIAPServices.StoreController();
            _store.OnStoreConnected += () => _store.FetchProducts(GoldShop.Packs.Select(p => new ProductDefinition(p.ProductId, ProductType.Consumable)).ToList());
            _store.OnStoreDisconnected += d => Debug.LogWarning("[Store] Disconnected: " + d.message);
            _store.OnProductsFetched += _ => { _fetched = true; _store.FetchPurchases(); };
            _store.OnProductsFetchFailed += f => Debug.LogWarning("[Store] Products not fetched: " + f.FailureReason);
            _store.OnPurchasesFetched += _ => { };
            _store.OnPurchasesFetchFailed += f => Debug.LogWarning("[Store] Purchases not fetched: " + f.message);
            _store.OnPurchasePending += OnPending;
            _store.OnPurchaseConfirmed += _ => { };
            _store.OnPurchaseFailed += OnFailed;
            _store.OnPurchaseDeferred += _ => Complete(new PurchaseOutcome { Error = "PENDING" }); // noloc
            _ = Connect();
        }

        async Task Connect()
        {
            try { await _store.Connect(); }
            catch (System.Exception e) { Debug.LogWarning("[Store] " + e.Message); }
        }

        public bool IsReady => _fetched;

        public string LocalizedPrice(string productId)
        {
            var product = _fetched ? _store.GetProductById(productId) : null;
            return product != null && product.availableToPurchase ? product.metadata.localizedPriceString : null;
        }

        public IReadOnlyList<UnfinishedPurchase> Unfinished =>
            _pending.Select(p => new UnfinishedPurchase { ProductId = p.Value.product, TransactionId = p.Key }).ToList();

        public Task<PurchaseOutcome> BuyAsync(string productId)
        {
            if (!_fetched) return Task.FromResult(new PurchaseOutcome { Error = "NOT_READY" }); // noloc
            // Already paid, not credited yet (Google refuses to sell it again until then): try crediting that order.
            var paid = _pending.FirstOrDefault(p => p.Value.product == productId);
            if (paid.Key != null) return Task.FromResult(new PurchaseOutcome { Ok = true, TransactionId = paid.Key });
            _buying?.TrySetResult(new PurchaseOutcome { Cancelled = true });
            _buying = new TaskCompletionSource<PurchaseOutcome>();
            _buyingProduct = productId;
            _store.PurchaseProduct(productId);
            return _buying.Task;
        }

        public void Finish(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId) || !_pending.TryGetValue(transactionId, out var p)) return;
            _pending.Remove(transactionId);
            _store.ConfirmPurchase(p.order);
        }

        void OnPending(PendingOrder order)
        {
            string product = order.CartOrdered.Items().FirstOrDefault()?.Product?.definition.id;
            // Google's purchase token: what the server checks with Google.
            string token = order.Info.Google?.PurchaseToken;
            if (string.IsNullOrEmpty(token)) token = order.Info.TransactionID;
            if (string.IsNullOrEmpty(product) || string.IsNullOrEmpty(token)) return;
            _pending[token] = (product, order);
            if (product == _buyingProduct) Complete(new PurchaseOutcome { Ok = true, TransactionId = token });
            else Store.UnfinishedFound?.Invoke();
        }

        void OnFailed(FailedOrder order)
        {
            bool cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
            if (!cancelled) Debug.LogWarning("[Store] Purchase failed: " + order.FailureReason + " " + order.Details);
            Complete(new PurchaseOutcome { Cancelled = cancelled, Error = cancelled ? null : order.FailureReason.ToString() });
        }

        void Complete(PurchaseOutcome outcome)
        {
            var buying = _buying;
            _buying = null;
            _buyingProduct = null;
            buying?.TrySetResult(outcome);
        }
    }
}
