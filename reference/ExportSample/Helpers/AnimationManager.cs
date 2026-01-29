using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ExportSample.Helpers
{
    /// <summary>
    /// Gestisce animazioni fluide per l'interfaccia utente
    /// </summary>
    public class AnimationManager : IDisposable
    {
        private Timer animationTimer;
        private Dictionary<Control, AnimationState> animations = new Dictionary<Control, AnimationState>();
        private const int ANIMATION_INTERVAL = 16; // ~60 FPS
        
        /// <summary>
        /// Costruttore di AnimationManager, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public AnimationManager()
        {
            animationTimer = new Timer();
            animationTimer.Interval = ANIMATION_INTERVAL;
            animationTimer.Tick += OnAnimationTick;
        }
        
        /// <summary>
        /// Avvia un'animazione fade-in su un controllo
        /// </summary>
        public void StartFadeIn(Control control, int duration = 300)
        {
            if (control == null) return;
            
            lock (animations)
            {
                animations[control] = new AnimationState
                {
                    Type = AnimationType.FadeIn,
                    StartTime = DateTime.Now,
                    Duration = duration,
                    StartValue = 0,
                    EndValue = 255,
                    Control = control
                };
            }
            
            if (!animationTimer.Enabled)
                animationTimer.Start();
        }
        
        /// <summary>
        /// Avvia una transizione di colore fluida
        /// </summary>
        public void StartColorTransition(Control control, Color fromColor, Color toColor, int duration = 200)
        {
            if (control == null) return;
            
            lock (animations)
            {
                animations[control] = new AnimationState
                {
                    Type = AnimationType.ColorTransition,
                    StartTime = DateTime.Now,
                    Duration = duration,
                    StartColor = fromColor,
                    EndColor = toColor,
                    Control = control
                };
            }
            
            if (!animationTimer.Enabled)
                animationTimer.Start();
        }
        
        /// <summary>
        /// Avvia un effetto pulse su un controllo
        /// </summary>
        public void StartPulse(Control control, int duration = 1000)
        {
            if (control == null) return;
            
            lock (animations)
            {
                animations[control] = new AnimationState
                {
                    Type = AnimationType.Pulse,
                    StartTime = DateTime.Now,
                    Duration = duration,
                    StartValue = 1.0,
                    EndValue = 1.05,
                    Control = control
                };
            }
            
            if (!animationTimer.Enabled)
                animationTimer.Start();
        }
        
        /// <summary>
        /// Avvia un effetto ripple dal punto di click
        /// </summary>
        public void StartRipple(Control control, Point clickPoint)
        {
            if (control == null) return;
            
            lock (animations)
            {
                animations[control] = new AnimationState
                {
                    Type = AnimationType.Ripple,
                    StartTime = DateTime.Now,
                    Duration = 500,
                    ClickPoint = clickPoint,
                    StartValue = 0,
                    EndValue = Math.Max(control.Width, control.Height),
                    Control = control
                };
            }
            
            control.Invalidate();
            
            if (!animationTimer.Enabled)
                animationTimer.Start();
        }
        
        /// <summary>
        /// Avvia un effetto di scala
        /// </summary>
        public void StartScale(Control control, double fromScale, double toScale, int duration = 150)
        {
            if (control == null) return;
            
            lock (animations)
            {
                animations[control] = new AnimationState
                {
                    Type = AnimationType.Scale,
                    StartTime = DateTime.Now,
                    Duration = duration,
                    StartValue = fromScale,
                    EndValue = toScale,
                    OriginalBounds = control.Bounds,
                    Control = control
                };
            }
            
            if (!animationTimer.Enabled)
                animationTimer.Start();
        }
        
        /// <summary>
        /// Callback WinForms per animation tick.
        /// </summary>
        private void OnAnimationTick(object sender, EventArgs e)
        {
            List<Control> completedAnimations = new List<Control>();
            
            lock (animations)
            {
                foreach (var kvp in animations.ToList())
                {
                    var control = kvp.Key;
                    var state = kvp.Value;
                    
                    if (control.IsDisposed)
                    {
                        completedAnimations.Add(control);
                        continue;
                    }
                    
                    var elapsed = (DateTime.Now - state.StartTime).TotalMilliseconds;
                    var progress = Math.Min(elapsed / state.Duration, 1.0);
                    
                    // Easing function (ease-in-out cubic)
                    var easedProgress = EaseInOutCubic(progress);
                    
                    try
                    {
                        switch (state.Type)
                        {
                            case AnimationType.FadeIn:
                                // Per fade-in, modifichiamo l'opacità del form se è un form
                                if (control is Form form)
                                {
                                    var opacity = Lerp(state.StartValue, state.EndValue, easedProgress) / 255.0;
                                    if (control.InvokeRequired)
                                        control.Invoke(new Action(() => form.Opacity = opacity));
                                    else
                                        form.Opacity = opacity;
                                }
                                break;
                                
                            case AnimationType.ColorTransition:
                                var r = (int)Lerp(state.StartColor.R, state.EndColor.R, easedProgress);
                                var g = (int)Lerp(state.StartColor.G, state.EndColor.G, easedProgress);
                                var b = (int)Lerp(state.StartColor.B, state.EndColor.B, easedProgress);
                                var newColor = Color.FromArgb(r, g, b);
                                
                                if (control.InvokeRequired)
                                    control.Invoke(new Action(() => control.BackColor = newColor));
                                else
                                    control.BackColor = newColor;
                                break;
                                
                            case AnimationType.Pulse:
                                // Il pulse è gestito nel paint event del controllo
                                state.CurrentValue = Lerp(state.StartValue, state.EndValue, easedProgress);
                                control.Invalidate();
                                
                                // Loop dell'animazione pulse
                                if (progress >= 1.0)
                                {
                                    state.StartTime = DateTime.Now;
                                    var temp = state.StartValue;
                                    state.StartValue = state.EndValue;
                                    state.EndValue = temp;
                                }
                                break;
                                
                            case AnimationType.Ripple:
                                state.CurrentValue = Lerp(state.StartValue, state.EndValue, easedProgress);
                                control.Invalidate();
                                break;
                                
                            case AnimationType.Scale:
                                var scale = Lerp(state.StartValue, state.EndValue, easedProgress);
                                var originalBounds = state.OriginalBounds;
                                var newWidth = (int)(originalBounds.Width * scale);
                                var newHeight = (int)(originalBounds.Height * scale);
                                var newX = originalBounds.X + (originalBounds.Width - newWidth) / 2;
                                var newY = originalBounds.Y + (originalBounds.Height - newHeight) / 2;
                                
                                if (control.InvokeRequired)
                                {
                                    control.Invoke(new Action(() =>
                                    {
                                        control.Bounds = new Rectangle(newX, newY, newWidth, newHeight);
                                    }));
                                }
                                else
                                {
                                    control.Bounds = new Rectangle(newX, newY, newWidth, newHeight);
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Animation error: {ex.Message}");
                        completedAnimations.Add(control);
                    }
                    
                    if (progress >= 1.0 && state.Type != AnimationType.Pulse)
                    {
                        completedAnimations.Add(control);
                    }
                }
                
                foreach (var control in completedAnimations)
                {
                    animations.Remove(control);
                }
                
                if (animations.Count == 0)
                {
                    animationTimer.Stop();
                }
            }
        }
        
        /// <summary>
        /// Funzione di easing cubic in-out per animazioni fluide
        /// </summary>
        private double EaseInOutCubic(double t)
        {
            return t < 0.5 
                ? 4 * t * t * t 
                : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }
        
        /// <summary>
        /// Interpolazione lineare tra due valori
        /// </summary>
        private double Lerp(double start, double end, double progress)
        {
            return start + (end - start) * progress;
        }
        
        /// <summary>
        /// Ottiene lo stato corrente dell'animazione per un controllo
        /// </summary>
        public AnimationState GetAnimationState(Control control)
        {
            lock (animations)
            {
                return animations.ContainsKey(control) ? animations[control] : null;
            }
        }
        
        /// <summary>
        /// Ferma tutte le animazioni per un controllo
        /// </summary>
        public void StopAnimation(Control control)
        {
            lock (animations)
            {
                if (animations.ContainsKey(control))
                {
                    animations.Remove(control);
                }
            }
        }
        
        /// <summary>
        /// Ferma tutte le animazioni
        /// </summary>
        public void StopAll()
        {
            lock (animations)
            {
                animations.Clear();
                animationTimer.Stop();
            }
        }
        
        /// <summary>
        /// Rilascia lo stato e chiude risorse gestite.
        /// </summary>
        public void Dispose()
        {
            StopAll();
            animationTimer?.Dispose();
        }
    }
    
    /// <summary>
    /// Tipi di animazione supportati
    /// </summary>
    public enum AnimationType
    {
        FadeIn,
        ColorTransition,
        Pulse,
        Ripple,
        Scale
    }
    
    /// <summary>
    /// Stato di un'animazione in corso
    /// </summary>
    public class AnimationState
    {
        /// <summary>
        /// Valore type esposto pubblicamente.
        /// </summary>
        public AnimationType Type { get; set; }
        /// <summary>
        /// Timestamp relativo a start time.
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// Valore duration esposto pubblicamente.
        /// </summary>
        public int Duration { get; set; }
        /// <summary>
        /// Valore start value esposto pubblicamente.
        /// </summary>
        public double StartValue { get; set; }
        /// <summary>
        /// Valore end value esposto pubblicamente.
        /// </summary>
        public double EndValue { get; set; }
        /// <summary>
        /// Valore current value esposto pubblicamente.
        /// </summary>
        public double CurrentValue { get; set; }
        /// <summary>
        /// Valore start color esposto pubblicamente.
        /// </summary>
        public Color StartColor { get; set; }
        /// <summary>
        /// Valore end color esposto pubblicamente.
        /// </summary>
        public Color EndColor { get; set; }
        /// <summary>
        /// Valore click point esposto pubblicamente.
        /// </summary>
        public Point ClickPoint { get; set; }
        /// <summary>
        /// Valore original bounds esposto pubblicamente.
        /// </summary>
        public Rectangle OriginalBounds { get; set; }
        /// <summary>
        /// Valore control esposto pubblicamente.
        /// </summary>
        public Control Control { get; set; }
    }
}
