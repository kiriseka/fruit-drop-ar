using System;
using UnityEngine;

/// <summary>
/// Shows what just happened to a fruit: sparkles and a bounce of the basket for a catch, a splash in the fruit's
/// colour where it hits the floor, and a short buzz of the phone for a miss or for catching fruit to avoid.
/// It only listens to the spawner, as <see cref="GameAudio"/> does. The particles are set up here in code, so
/// there are no effect prefabs to keep in step with it.
/// </summary>
public class CatchEffects : MonoBehaviour
{
    public enum Effect
    {
        Sparkle,
        Splash,
        Bounce,
        Vibration,
        Confetti,
    }

    [SerializeField]
    [Tooltip("Reports each fruit that is caught or reaches the floor.")]
    FruitSpawner m_Spawner;

    [SerializeField]
    [Tooltip("Gives the basket, for the bounce and for where the sparkles appear.")]
    FloorBasketFollower m_Floor;

    [SerializeField]
    [Tooltip("Optional. Reports Game Over, for the confetti on a new best score.")]
    GameStateManager m_Game;

    [SerializeField]
    [Tooltip("Optional. Tells whether the round beat the best score.")]
    ScoreManager m_Score;

    [SerializeField]
    [Tooltip("Material for the particles: a soft dot that takes its colour from each particle.")]
    Material m_ParticleMaterial;

    [SerializeField]
    [Tooltip("Colour of the sparkles for a catch.")]
    Color m_SparkleColor = new Color(1f, 0.95f, 0.6f, 1f);

    [SerializeField]
    [Tooltip("How far the basket squashes on a catch, as a share of its height.")]
    float m_BounceAmount = 0.15f;

    [SerializeField]
    [Tooltip("Seconds the bounce lasts.")]
    float m_BounceSeconds = 0.25f;

    [SerializeField]
    [Tooltip("Milliseconds the phone vibrates for a miss or for catching fruit to avoid.")]
    int m_VibrationMilliseconds = 40;

    ParticleSystem m_Sparkle;
    ParticleSystem m_Splash;
    ParticleSystem m_Confetti;
    GameObject m_Basket;
    Transform[] m_BounceTargets;
    float m_BounceTime = -1f;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject m_Vibrator;
#endif

    /// <summary>
    /// Called each time an effect starts.
    /// </summary>
    public event Action<Effect> played;

    void Awake()
    {
        // Sparkles fly up out of the basket and fall back a little; the splash spreads low over the floor.
        // The sizes are large for the basket (26 cm across) on purpose: the dot texture is soft, so a particle
        // looks about a third of its size, and on a phone the basket is often more than a meter away.
        m_Sparkle = CreateParticles("Catch Sparkle", 35f, 0.5f, 1.0f, 0.03f, 0.06f, 0.5f, 0.3f);
        m_Splash = CreateParticles("Miss Splash", 70f, 0.4f, 0.9f, 0.04f, 0.08f, 0.45f, 1f);

        // Confetti in the fruits' colours, thrown up in front of the phone and drifting down.
        m_Confetti = CreateParticles("New Best Confetti", 50f, 1.4f, 2.4f, 0.025f, 0.045f, 1.8f, 0.5f);
        var confettiMain = m_Confetti.main;
        var colours = new Gradient();
        colours.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.15f, 0.15f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.05f), 0.25f),
                new GradientColorKey(new Color(1f, 0.88f, 0.15f), 0.5f),
                new GradientColorKey(new Color(0.45f, 0.78f, 0.2f), 0.75f),
                new GradientColorKey(Color.white, 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        confettiMain.startColor = new ParticleSystem.MinMaxGradient(colours) { mode = ParticleSystemGradientMode.RandomColor };
    }

    void OnEnable()
    {
        if (m_Spawner != null)
            m_Spawner.fruitLanded += OnFruitLanded;

        if (m_Game != null)
            m_Game.stateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        if (m_Spawner != null)
            m_Spawner.fruitLanded -= OnFruitLanded;

        if (m_Game != null)
            m_Game.stateChanged -= OnStateChanged;

        ResetBounce();
    }

    void OnFruitLanded(Fruit fruit, bool caught)
    {
        bool isHazard = fruit.type.isHazard;
        var basket = m_Floor != null && m_Floor.basket != null ? m_Floor.basket.transform : null;
        if (caught && basket != null)
        {
            // Good fruit sparkles; fruit to avoid bursts in its own colour at the basket instead.
            var rim = basket.position + Vector3.up * 0.1f;
            if (isHazard)
                Emit(m_Splash, Effect.Splash, rim, fruit.type.color, 16);
            else
                Emit(m_Sparkle, Effect.Sparkle, rim, m_SparkleColor, 16);

            StartBounce(basket);
        }
        else if (!caught)
        {
            Emit(m_Splash, Effect.Splash, fruit.transform.position, fruit.type.color, 16);
        }

        // Letting fruit to avoid fall is the right move, so it is not marked as a mistake.
        if (caught == isHazard)
            Vibrate();
    }

    void OnStateChanged(GameStateManager.GameState state)
    {
        if (state != GameStateManager.GameState.GameOver || m_Score == null || !m_Score.isNewHighScore)
            return;

        var camera = Camera.main;
        if (camera == null)
            return;

        // A little below eye level and in front of the phone, so it rises into view and falls past it.
        var position = camera.transform.position + camera.transform.forward * 1.2f - Vector3.up * 0.3f;
        m_Confetti.Emit(new ParticleSystem.EmitParams { position = position, applyShapeToPosition = true }, 70);
        played?.Invoke(Effect.Confetti);
    }

    void Update()
    {
        if (m_BounceTime < 0f)
            return;

        m_BounceTime += Time.deltaTime;
        float phase = Mathf.Clamp01(m_BounceTime / Mathf.Max(0.01f, m_BounceSeconds));
        if (phase >= 1f)
        {
            ResetBounce();
            return;
        }

        // Shorter and a little wider, then back: the basket gives under the weight of the fruit.
        float squash = m_BounceAmount * Mathf.Sin(phase * Mathf.PI);
        SetBounceScale(new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f));
    }

    // The basket's root is placed by FloorBasketFollower every frame, so the bounce only ever scales its
    // children. The shadow is left out: it lies on the floor and should not squash.
    void StartBounce(Transform basket)
    {
        if (m_Basket != basket.gameObject)
        {
            ResetBounce();
            m_Basket = basket.gameObject;
            var targets = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in basket)
            {
                if (child.name != "Shadow")
                    targets.Add(child);
            }

            m_BounceTargets = targets.ToArray();
        }

        m_BounceTime = 0f;
        played?.Invoke(Effect.Bounce);
    }

    void ResetBounce()
    {
        m_BounceTime = -1f;
        SetBounceScale(Vector3.one);
    }

    void SetBounceScale(Vector3 scale)
    {
        if (m_BounceTargets == null)
            return;

        foreach (var target in m_BounceTargets)
        {
            if (target != null)
                target.localScale = scale;
        }
    }

    void Emit(ParticleSystem particles, Effect effect, Vector3 position, Color color, int count)
    {
        // A little lighter than the fruit, so dark fruit still shows against a dark floor.
        color = Color.Lerp(color, Color.white, 0.2f);
        color.a = 1f;
        particles.Emit(new ParticleSystem.EmitParams { position = position, startColor = color, applyShapeToPosition = true }, count);
        played?.Invoke(effect);
    }

    void Vibrate()
    {
        if (!GameSettings.vibrationOn)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Handheld.Vibrate is a fixed, much longer buzz, so the Android vibrator is asked for a short one.
        // Handheld.Vibrate stays as the fallback, which also makes Unity add the VIBRATE permission.
        try
        {
            if (m_Vibrator == null)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                m_Vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            m_Vibrator.Call("vibrate", (long)m_VibrationMilliseconds);
        }
        catch (Exception e)
        {
            Debug.Log($"[CatchEffects] Short vibration not available, using the default one: {e.Message}");
            Handheld.Vibrate();
        }
#elif UNITY_IOS && !UNITY_EDITOR
        Handheld.Vibrate();
#endif
        played?.Invoke(Effect.Vibration);
    }

    // One particle system per kind of burst. Particles live in world space, so a burst stays where it happened
    // while the next one is sent somewhere else.
    ParticleSystem CreateParticles(string name, float coneAngle, float minSpeed, float maxSpeed, float minSize, float maxSize, float lifetime, float gravity)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);

        // A cone sends particles along its local forward, so point that up.
        holder.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var particles = holder.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;

        var emission = particles.emission;
        emission.enabled = false;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.04f;

        // Fade out over the second half of each particle's life.
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = m_ParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }
}
