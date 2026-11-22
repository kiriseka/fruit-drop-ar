using UnityEditor;
using UnityEngine;

/// <summary>
/// Development tool, not part of the game. Adds a soft sparkle trail to the golden apple prefab, so it stands
/// apart from the lemon while it falls. Running it again rebuilds the trail with the values here.
/// </summary>
public static class GoldenTrailBuilder
{
    const string k_PrefabPath = "Assets/Prefabs/Fruit/GoldenApple.prefab";
    const string k_MaterialPath = "Assets/Materials/EffectParticle.mat";
    const string k_TrailName = "Sparkle Trail";

    [MenuItem("Fruit Drop/Build Golden Apple Trail")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
        var old = root.transform.Find(k_TrailName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        var trail = new GameObject(k_TrailName);
        trail.transform.SetParent(root.transform, false);
        var particles = trail.AddComponent<ParticleSystem>();

        // Sparkles left behind in the air as the apple falls, fading and shrinking within half a second.
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        // Sizes are large for a sparkle because the dot texture is soft and the apple is usually 1-2 m away;
        // smaller ones were only a few pixels in a 1080 px wide view.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;
        var gold = new Gradient();
        gold.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.97f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.2f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(gold) { mode = ParticleSystemGradientMode.RandomColor };

        var emission = particles.emission;
        emission.rateOverTime = 40f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = fadeGradient;

        var shrink = particles.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

        var renderer = trail.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log($"[GoldenTrailBuilder] Added a sparkle trail to {k_PrefabPath}");
    }
}
