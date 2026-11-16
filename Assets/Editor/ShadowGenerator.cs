using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development tool, not part of the game. Draws the basket's shadow texture from a formula: a soft shadow that
/// fades out around the basket, a darker core under it, and a thin dark ring where the base meets the floor.
/// The ring is what makes the basket look as if it stands on the floor instead of floating just above it.
/// Run it again after changing the formula; the file keeps its name, so the material needs no rewiring.
/// </summary>
public static class ShadowGenerator
{
    const string k_Path = "Assets/Textures/BasketShadow.png";
    const int k_Size = 256;

    // The shadow quad is 0.42 m across and the basket's base 0.26 m, so the base ends this far along the way
    // from the middle of the texture to its edge.
    const float k_BaseEdge = 0.13f / 0.21f;

    [MenuItem("Fruit Drop/Generate Shadow Texture")]
    public static void Generate()
    {
        var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
        var pixels = new Color32[k_Size * k_Size];
        float half = (k_Size - 1) * 0.5f;
        for (int y = 0; y < k_Size; y++)
        {
            for (int x = 0; x < k_Size; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                pixels[y * k_Size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * Darkness(r)));
            }
        }

        texture.SetPixels32(pixels);
        File.WriteAllBytes(k_Path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(k_Path);
        Debug.Log($"[ShadowGenerator] Wrote {k_Path}, {k_Size} px: darkness {Darkness(0f):F2} in the middle, " +
            $"{Darkness(k_BaseEdge + 0.015f):F2} on the contact ring, {Darkness(0.8f):F2} at 0.8 of the way out, {Darkness(1f):F2} at the edge");
    }

    /// <summary>
    /// How dark the shadow is, from 0 to 1, at <paramref name="r"/>: 0 in the middle of the quad, 1 at its edge.
    /// </summary>
    public static float Darkness(float r)
    {
        // Soft shadow, the same as the V0.3 texture so the basket's shadow never gets weaker: 0.70 under the
        // basket, fading to nothing at the edge of the quad.
        float soft = 0.70f * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, r));

        // Darker core under the basket, gone a little past the base.
        float core = 0.15f * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(k_BaseEdge - 0.25f, k_BaseEdge + 0.06f, r));

        // Contact ring: a thin dark line just outside the base.
        float offset = (r - (k_BaseEdge + 0.015f)) / 0.025f;
        float ring = 0.22f * Mathf.Exp(-0.5f * offset * offset);
        return Mathf.Clamp01(soft + core + ring);
    }
}
