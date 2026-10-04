using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;

namespace MummyEscape.World
{
    /// <summary>
    /// The rival's ghost in a duel: a pale, see-through mummy that glides from tile to tile. It only shows where the
    /// player's torch reaches (a ghost in the dark would give the way away).
    /// </summary>
    public sealed class GhostView : MonoBehaviour
    {
        static readonly Color Tint = new Color(0.62f, 1f, 0.95f, 1f);
        const float MaxAlpha = 0.55f;

        SpriteRenderer _sprite;
        Vector3 _target;
        bool _visible;
        float _alpha;

        public void Init(ArtLibrary art, Material unlit)
        {
            _sprite = new GameObject("Body").AddComponent<SpriteRenderer>();
            _sprite.transform.SetParent(transform, false);
            if (unlit != null) _sprite.sharedMaterial = unlit; // glows faintly whatever the light
            _sprite.sortingOrder = 9; // under the player
            _sprite.sprite = art.Mummy(SkinCatalog.Classic);
            Hide();
        }

        public void Snap(Cell c)
        {
            _target = MazeView.CellToWorld(c);
            transform.position = _target;
        }

        /// <summary>Where the ghost stands now, and whether the player can see that tile.</summary>
        public void Show(Cell c, bool visible)
        {
            var to = MazeView.CellToWorld(c);
            // A far jump (portal, fall, other floor) is not walked: the ghost fades out and reappears there.
            if ((to - transform.position).sqrMagnitude > 2.5f * 2.5f && _alpha > 0.01f) _alpha = 0f;
            if ((to - transform.position).sqrMagnitude > 2.5f * 2.5f) transform.position = to;
            if (to.x < _target.x - 0.01f) _sprite.flipX = true;
            else if (to.x > _target.x + 0.01f) _sprite.flipX = false;
            _target = to;
            _visible = visible;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            _visible = false;
            _alpha = 0f;
            if (_sprite != null) _sprite.color = new Color(Tint.r, Tint.g, Tint.b, 0f);
            gameObject.SetActive(false);
        }

        void Update()
        {
            transform.position = Vector3.MoveTowards(transform.position, _target, Time.deltaTime * 9f);
            _alpha = Mathf.MoveTowards(_alpha, _visible ? MaxAlpha : 0f, Time.deltaTime * 3f);
            float wave = 1f + Mathf.Sin(Time.time * 2.4f) * 0.08f;
            _sprite.color = new Color(Tint.r, Tint.g, Tint.b, _alpha * wave);
            _sprite.transform.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 2f) * 0.05f + 0.04f, 0f);
        }
    }
}
