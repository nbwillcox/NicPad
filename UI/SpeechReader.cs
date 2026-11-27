using System.Speech.Synthesis;

namespace NicPad.UI;

/// <summary>Read Aloud via the Windows speech engine (SAPI). Text is spoken as plain text, never parsed as SSML.</summary>
internal sealed class SpeechReader : IDisposable
{
    private SpeechSynthesizer? synth;

    public event EventHandler? Finished;

    public bool IsSpeaking => synth?.State == SynthesizerState.Speaking;

    public IReadOnlyList<string> VoiceNames()
    {
        try
        {
            EnsureSynth();
#pragma warning disable CA1304 // We want every installed voice, not just those for the current culture.
            return synth!.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();
#pragma warning restore CA1304
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    public void Speak(string text, string? voice, int rate)
    {
        EnsureSynth();
        synth!.SpeakAsyncCancelAll();
        if (!string.IsNullOrEmpty(voice))
        {
            try
            {
                synth.SelectVoice(voice);
            }
            catch (ArgumentException)
            {
                // Voice was uninstalled; keep the default.
            }
        }

        synth.Rate = Math.Clamp(rate, -10, 10);
        synth.SpeakAsync(text);
    }

    public void Stop() => synth?.SpeakAsyncCancelAll();

    public void Dispose()
    {
        if (synth is not null)
        {
            synth.SpeakAsyncCancelAll();
            synth.Dispose();
            synth = null;
        }
    }

    private void EnsureSynth()
    {
        if (synth is not null)
        {
            return;
        }

        synth = new SpeechSynthesizer();
        synth.SetOutputToDefaultAudioDevice();
        synth.SpeakCompleted += (_, _) => Finished?.Invoke(this, EventArgs.Empty);
    }
}
