using YukkuriMovieMaker.Player.Audio;
using YukkuriMovieMaker.Player.Audio.Effects;
using System.Collections.ObjectModel;
using System;

namespace ymm4_guiequalizer
{
    internal class GuiEqualizerProcessor : AudioEffectProcessorBase
    {
        readonly GuiEqualizerEffect item;
        BiquadFilter[] filters;
        readonly ReadOnlyObservableCollection<EQBand> bands;

        private readonly OverSampler overSamplerL;
        private readonly OverSampler overSamplerR;

        public override int Hz => Input?.Hz ?? 0;
        public override long Duration => Input?.Duration ?? 0;

        public GuiEqualizerProcessor(GuiEqualizerEffect item, TimeSpan duration)
        {
            this.item = item;
            bands = new(item.Bands);
            filters = new BiquadFilter[bands.Count];
            for (int i = 0; i < filters.Length; i++)
            {
                filters[i] = new BiquadFilter();
            }
            overSamplerL = new OverSampler();
            overSamplerR = new OverSampler();
        }

        protected override int read(float[] destBuffer, int offset, int count)
        {
            if (Input is null) return 0;
            int readCount = Input.Read(destBuffer, offset, count);

            bool useHighQuality = EqualizerSettings.Default.HighQualityMode;

            if (filters.Length != bands.Count)
            {
                System.Array.Resize(ref filters, bands.Count);
                for (int k = 0; k < filters.Length; k++)
                {
                    if (filters[k] == null) filters[k] = new BiquadFilter();
                }
            }

            for (int i = 0; i < readCount; i += 2)
            {
                long currentFrame = (Position + i) / 2;
                long totalFrames = Duration / 2;

                float leftSample = destBuffer[offset + i];
                float rightSample = destBuffer[offset + i + 1];

                if (useHighQuality)
                {
                    float[] upsampledL = overSamplerL.Upsample(leftSample);
                    float[] upsampledR = overSamplerR.Upsample(rightSample);

                    for (int s = 0; s < 2; s++)
                    {
                        for (int j = 0; j < bands.Count; j++)
                        {
                            var band = bands[j];
                            if (band.IsEnabled)
                            {
                                var freq = (float)band.Frequency.GetValue(currentFrame, totalFrames, Hz);
                                var gain = (float)band.Gain.GetValue(currentFrame, totalFrames, Hz);
                                var q = (float)band.Q.GetValue(currentFrame, totalFrames, Hz);
                                filters[j].SetCoefficients(band.Type, Hz * 2, freq, gain, q);

                                if (band.StereoMode is StereoMode.Stereo or StereoMode.Left)
                                    upsampledL[s] = filters[j].Process(upsampledL[s], 0);
                                if (band.StereoMode is StereoMode.Stereo or StereoMode.Right)
                                    upsampledR[s] = filters[j].Process(upsampledR[s], 1);
                            }
                        }
                    }
                    leftSample = overSamplerL.Downsample(upsampledL);
                    rightSample = overSamplerR.Downsample(upsampledR);
                }
                else
                {
                    for (int j = 0; j < bands.Count; j++)
                    {
                        var band = bands[j];
                        if (band.IsEnabled)
                        {
                            var freq = (float)band.Frequency.GetValue(currentFrame, totalFrames, Hz);
                            var gain = (float)band.Gain.GetValue(currentFrame, totalFrames, Hz);
                            var q = (float)band.Q.GetValue(currentFrame, totalFrames, Hz);
                            filters[j].SetCoefficients(band.Type, Hz, freq, gain, q);

                            if (band.StereoMode is StereoMode.Stereo or StereoMode.Left)
                                leftSample = filters[j].Process(leftSample, 0);
                            if (band.StereoMode is StereoMode.Stereo or StereoMode.Right)
                                rightSample = filters[j].Process(rightSample, 1);
                        }
                    }
                }
                destBuffer[offset + i] = leftSample;
                destBuffer[offset + i + 1] = rightSample;
            }
            return readCount;
        }

        protected override void seek(long position)
        {
            Input?.Seek(position);
            foreach (var filter in filters) filter?.Reset();
            overSamplerL.Reset();
            overSamplerR.Reset();
        }
    }

    internal class OverSampler
    {
        private float last_in;
        private float last_out;
        public float[] Upsample(float input)
        {
            float[] output = new float[2];
            output[0] = (last_in + input) * 0.5f;
            output[1] = input;
            last_in = input;
            return output;
        }
        public float Downsample(float[] input)
        {
            float output = (last_out + input[0]) * 0.5f;
            last_out = input[1];
            return output;
        }
        public void Reset()
        {
            last_in = 0;
            last_out = 0;
        }
    }

    internal class BiquadFilter
    {
        private float a1, a2, b0, b1, b2;
        private float x1L, x2L, y1L, y2L;
        private float x1R, x2R, y1R, y2R;

        public void Reset() { x1L = x2L = y1L = y2L = 0; x1R = x2R = y1R = y2R = 0; }

        public void SetCoefficients(FilterType type, float sampleRate, float freq, float gainDb, float q)
        {
            if (freq <= 20) freq = 20;
            if (q <= 0) q = 0.01f;
            if (freq >= sampleRate / 2) freq = sampleRate / 2 - 1;

            double omega = 2.0 * System.Math.PI * freq / sampleRate;
            double sinOmega = System.Math.Sin(omega);
            double cosOmega = System.Math.Cos(omega);
            double alpha = sinOmega / (2.0 * q);
            double aVal = System.Math.Pow(10, gainDb / 40.0);
            double a0 = 1;

            switch (type)
            {
                case FilterType.Peak:
                    b0 = (float)(1.0 + alpha * aVal);
                    b1 = (float)(-2.0 * cosOmega);
                    b2 = (float)(1.0 - alpha * aVal);
                    a0 = 1.0 + alpha / aVal;
                    a1 = (float)(-2.0 * cosOmega);
                    a2 = (float)(1.0 - alpha / aVal);
                    break;
                case FilterType.LowShelf:
                    b0 = (float)(aVal * ((aVal + 1.0) - (aVal - 1.0) * cosOmega + 2.0 * System.Math.Sqrt(aVal) * alpha));
                    b1 = (float)(2.0 * aVal * ((aVal - 1.0) - (aVal + 1.0) * cosOmega));
                    b2 = (float)(aVal * ((aVal + 1.0) - (aVal - 1.0) * cosOmega - 2.0 * System.Math.Sqrt(aVal) * alpha));
                    a0 = (aVal + 1.0) + (aVal - 1.0) * cosOmega + 2.0 * System.Math.Sqrt(aVal) * alpha;
                    a1 = (float)(-2.0 * ((aVal - 1.0) + (aVal + 1.0) * cosOmega));
                    a2 = (float)((aVal + 1.0) + (aVal - 1.0) * cosOmega - 2.0 * System.Math.Sqrt(aVal) * alpha);
                    break;
                case FilterType.HighShelf:
                    b0 = (float)(aVal * ((aVal + 1.0) + (aVal - 1.0) * cosOmega + 2.0 * System.Math.Sqrt(aVal) * alpha));
                    b1 = (float)(-2.0 * aVal * ((aVal - 1.0) + (aVal + 1.0) * cosOmega));
                    b2 = (float)(aVal * ((aVal + 1.0) + (aVal - 1.0) * cosOmega - 2.0 * System.Math.Sqrt(aVal) * alpha));
                    a0 = (aVal + 1.0) - (aVal - 1.0) * cosOmega + 2.0 * System.Math.Sqrt(aVal) * alpha;
                    a1 = (float)(2.0 * ((aVal - 1.0) - (aVal + 1.0) * cosOmega));
                    a2 = (float)((aVal + 1.0) + (aVal - 1.0) * cosOmega - 2.0 * System.Math.Sqrt(aVal) * alpha);
                    break;
            }
            this.b0 = (float)(b0 / a0); this.b1 = (float)(b1 / a0); this.b2 = (float)(b2 / a0);
            this.a1 = (float)(a1 / a0); this.a2 = (float)(a2 / a0);
        }

        public float Process(float input, int channel)
        {
            if (channel == 0)
            {
                float output = b0 * input + b1 * x1L + b2 * x2L - a1 * y1L - a2 * y2L;
                x2L = x1L; x1L = input; y2L = y1L; y1L = output;
                return output;
            }
            else
            {
                float output = b0 * input + b1 * x1R + b2 * x2R - a1 * y1R - a2 * y2R;
                x2R = x1R; x1R = input; y2R = y1R; y1R = output;
                return output;
            }
        }
    }
}