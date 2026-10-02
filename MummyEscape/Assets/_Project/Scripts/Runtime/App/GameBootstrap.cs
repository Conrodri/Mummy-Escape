using UnityEngine;

namespace MummyEscape.App
{
    /// <summary>
    /// The only component placed in the scene. Holds asset references (so they are included in builds) and
    /// creates the <see cref="GameApp"/>. Everything else is built from code.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("URP 2D 'Sprite-Lit-Default' material so sprites react to 2D lights.")]
        [SerializeField] Material spriteLitMaterial;
        [Tooltip("Optional music loop. A synthesized ambient loop is used when empty.")]
        [SerializeField] AudioClip music;

        void Awake()
        {
            if (GameApp.I == null) GameApp.Create(spriteLitMaterial, music);
            Destroy(gameObject);
        }
    }
}
