using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development tool, not part of the game. Makes the game's sound effects from formulas and writes them as WAV
/// files to Assets/Audio, so the project needs no recorded or downloaded sounds. Run it again after changing a
/// formula; the files keep their names, so nothing has to be wired again.
/// </summary>
public static class SoundGenerator
{
    const int k_SampleRate = 44100;
    const string k_Folder = "Assets/Audio";

    [MenuItem("Fruit Drop/Generate Sounds")]
    public static void Generate()
    {
        Directory.CreateDirectory(k_Folder);
        Write("CatchPop", CatchPop());
        Write("MissThud", MissThud());
        Write("SpawnCue", SpawnCue());
        Write("RottenBuzz", RottenBuzz());
        Write("GameOver", GameOver());
        AssetDatabase.Refresh();
        Debug.Log($"[SoundGenerator] Wrote 5 sounds to {k_Folder}");
    }

    // A short rising chirp: bright and quick, so many catches in a row stay pleasant.
    static float[] CatchPop()
    {
        return Tone(0.12f, t => Mathf.Lerp(500f, 950f, t / 0.12f), t => Attack(t, 0.004f) * Mathf.Exp(-t / 0.035f), Sine, 0.8f);
    }

    // A low falling note with a puff of muffled noise, like fruit hitting the floor.
    static float[] MissThud()
    {
        var samples = Tone(0.28f, t => Mathf.Lerp(120f, 50f, t / 0.28f), t => Mathf.Exp(-t / 0.07f), Sine, 1f);
        var random = new System.Random(7);
        float filtered = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)k_SampleRate;
            filtered += 0.1f * ((float)random.NextDouble() * 2f - 1f - filtered);
            samples[i] += 1.2f * filtered * Mathf.Exp(-t / 0.03f);
        }

        return Normalize(samples, 0.85f);
    }

    // A soft falling blip. It is the one sound placed in the room, so it is kept short and gentle.
    static float[] SpawnCue()
    {
        return Tone(0.16f, t => Mathf.Lerp(1400f, 1000f, t / 0.16f), t => Attack(t, 0.015f) * Mathf.Exp(-t / 0.05f),
            x => Mathf.Sin(x) + 0.2f * Mathf.Sin(2f * x), 0.5f);
    }

    // A rough, wobbling low tone: clearly a mistake, and nothing like the catch.
    static float[] RottenBuzz()
    {
        const float length = 0.32f;
        return Tone(length, t => Mathf.Lerp(150f, 110f, t / length),
            t => Attack(t, 0.005f) * Mathf.Clamp01((length - t) / 0.06f) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 28f * t)),
            x => (float)Math.Tanh(4.0 * Math.Sin(x)), 0.5f);
    }

    // Three notes going down (G, E, C), the last one held.
    static float[] GameOver()
    {
        float[] starts = { 0f, 0.22f, 0.44f };
        float[] frequencies = { 392f, 329.63f, 261.63f };
        float[] decays = { 0.18f, 0.18f, 0.30f };
        var samples = new float[(int)(1.1f * k_SampleRate)];
        for (int note = 0; note < starts.Length; note++)
        {
            int first = (int)(starts[note] * k_SampleRate);
            for (int i = first; i < samples.Length; i++)
            {
                float t = (i - first) / (float)k_SampleRate;
                float x = 2f * Mathf.PI * frequencies[note] * t;
                samples[i] += Attack(t, 0.008f) * Mathf.Exp(-t / decays[note]) * (Mathf.Sin(x) + 0.3f * Mathf.Sin(3f * x) + 0.1f * Mathf.Sin(5f * x));
            }
        }

        return Normalize(samples, 0.6f);
    }

    static float Sine(float x) => Mathf.Sin(x);

    static float Attack(float t, float seconds) => Mathf.Clamp01(t / seconds);

    // The phase is added up sample by sample, so a changing frequency glides instead of clicking.
    static float[] Tone(float seconds, Func<float, float> frequency, Func<float, float> envelope, Func<float, float> wave, float peak)
    {
        var samples = new float[(int)(seconds * k_SampleRate)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)k_SampleRate;
            phase += 2f * Mathf.PI * frequency(t) / k_SampleRate;
            samples[i] = envelope(t) * wave(phase);
        }

        return Normalize(samples, peak);
    }

    static float[] Normalize(float[] samples, float peak)
    {
        float max = 0.0001f;
        foreach (float sample in samples)
            max = Mathf.Max(max, Mathf.Abs(sample));

        // Fade the last 5 ms to zero so the end never clicks.
        int fade = (int)(0.005f * k_SampleRate);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= peak / max;
            if (i >= samples.Length - fade)
                samples[i] *= (samples.Length - 1 - i) / (float)fade;
        }

        return samples;
    }

    // 16-bit mono PCM.
    static void Write(string name, float[] samples)
    {
        using var writer = new BinaryWriter(File.Create($"{k_Folder}/{name}.wav"));
        int dataBytes = samples.Length * 2;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(k_SampleRate);
        writer.Write(k_SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (float sample in samples)
            writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
    }
}
