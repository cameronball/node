using System;
using System.Linq;
using UnityEngine;
using Utility;
using View.Control;

namespace View.Items
{
    /// <summary>
    /// Allows easy modification of the color of the attached game object's material.
    /// </summary>
    public class Colorizer : MonoBehaviour
    {
        // Defaults
        public float DarkBrightnessScale => GameDef.Get.DarkBrightnessScale;

        public float TransitionTime => GameDef.Get.ColorTransitionTime;
        public LeanTweenType Ease => GameDef.Get.ColorEase;

        public Color PrimaryColor
        {
            get { return _primaryColor; }
            set {
                _previousColor = _primaryColor;
                _primaryColor = value;
                _primaryHsb = new HsbColor(_primaryColor);
                _darkHsb = new HsbColor(_primaryHsb.H, _primaryHsb.S, _primaryHsb.B*DarkBrightnessScale, _primaryHsb.A);
                CurrentColor = _primaryColor;
            }
        }

        private Color CurrentColor
        {
            get { return _material.color; }
            set {
                _material.color = value;

                foreach (var material in _childMaterials) {
                    material.color = value;
                }
            }
        }

        private HsbColor CurrentHsb
        {
            get { return new HsbColor(_material.color); }
            set { _material.color = value.ToColor(); }
        }

        private Color _primaryColor;
        private Color _previousColor;

        private HsbColor _primaryHsb;
        private HsbColor _darkHsb;

        private Material _material;

        private Material[] _childMaterials;

        private void Awake()
        {
            _material = GetComponent<Renderer>().material;
            _childMaterials = GetComponentsInChildren<Renderer>()
                .Where(r => r.GetComponent<ArcView>() == null)
                .Select(r => r.material)
                .ToArray();

            PrimaryColor = _material.color;
        }

        public void Highlight()
        {
            Highlight(TransitionTime, 0f, Ease);
        }

        public void Highlight(float time)
        {
            Highlight(time, 0f, Ease);
        }

        public void Highlight(float time, float delay, LeanTweenType ease)
        {
            _previousColor = CurrentColor;

            if (time < float.Epsilon) {
                CurrentColor = Brightness(CurrentColor, _primaryHsb.B);
                return;
            }

            LeanTween.value(gameObject, CurrentHsb.B, _primaryHsb.B, time)
                .setOnUpdate(b => CurrentColor = Brightness(CurrentColor, b))
                .setDelay(delay)
                .setEase(ease);
        }

        public void Darken()
        {
            Darken(TransitionTime, 0f, Ease);
        }

        public void Darken(float time)
        {
            Darken(time, 0f, Ease);
        }

        public void Darken(float time, float delay, LeanTweenType ease)
        {
            _previousColor = CurrentColor;

            if (time < float.Epsilon) {
                CurrentColor = Brightness(CurrentColor, _darkHsb.B);
                return;
            }

            LeanTween.value(gameObject, CurrentHsb.B, _darkHsb.B, time)
                .setOnUpdate(b => CurrentColor = Brightness(CurrentColor, b))
                .setDelay(delay)
                .setEase(ease);
        }

        public void Appear()
        {
            Appear(TransitionTime, 0f, Ease);
        }

        public void Appear(float time)
        {
            Appear(time, 0f, Ease);
        }

        public void Appear(float time, float delay, LeanTweenType ease, float animationSpeed = 1f, float delayScale = 1f)
        {
            _previousColor = CurrentColor;

            if (time < float.Epsilon) {
                CurrentColor = Alpha(CurrentColor, _primaryColor.a);
                return;
            }

            LeanTween.alpha(gameObject, _primaryColor.a, time * animationSpeed)
                .setDelay(delay * delayScale)
                .setEase(ease);
        }

        public void Fade(Action onComplete = null)
        {
            Fade(TransitionTime, 0f, Ease, onComplete);
        }

        public void Fade(float time, Action onComplete = null)
        {
            Fade(time, 0f, Ease, onComplete);
        }

        public void Fade(float time, float delay, LeanTweenType ease, Action onComplete = null)
        {
            onComplete = onComplete ?? (() => {});
            _previousColor = CurrentColor;

            if (time < float.Epsilon) {
                CurrentColor = Alpha(CurrentColor, 0f);
                return;
            }
            
            LeanTween.alpha(gameObject, 0f, time)
                .setDelay(delay)
                .setEase(ease)
                .setOnComplete(onComplete);
        }

        public void PulseAppear(float time)
        {
            _previousColor = CurrentColor;

            LeanTween.alpha(gameObject, _primaryColor.a + 0.1f, time)
                .setEase(LeanTweenType.easeInOutSine)
                .setOnComplete(() => {
                    LeanTween.alpha(gameObject, _primaryColor.a, time)
                        .setEase(LeanTweenType.easeInOutSine)
                        .setLoopPingPong(-1);
                });
        }

        public void Previous(float time, float delay, LeanTweenType ease)
        {
            LeanTween.alpha(gameObject, _previousColor.a, time)
                .setDelay(delay)
                .setEase(ease);
        }

        public static Color Alpha(Color color, float a)
        {
            return new Color(color.r, color.g, color.b, a);
        }

        public static Color Brightness(Color color, float b)
        {
            return new HsbColor(color) {B = b}.ToColor();
        }
    }
}
