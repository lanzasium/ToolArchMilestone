using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ExportSample.Helpers
{
    /// <summary>
    /// Helper per effetti UI avanzati e uniformi
    /// </summary>
    public static class UIEffects
    {
        /// <summary>
        /// Crea un percorso per un rettangolo arrotondato
        /// </summary>
        public static GraphicsPath GetRoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            
            // Assicura che il raggio non sia troppo grande
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));
            
            // Top left arc
            path.AddArc(arc, 180, 90);
            
            // Top right arc
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            
            // Bottom right arc
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            
            // Bottom left arc
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            
            path.CloseFigure();
            return path;
        }
        
        /// <summary>
        /// Crea un gradiente sottile per sfondi
        /// </summary>
        public static LinearGradientBrush CreateSubtleGradient(Rectangle rect, Color color1, Color color2)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return null;
                
            return new LinearGradientBrush(
                rect,
                color1,
                color2,
                LinearGradientMode.Vertical);
        }
        
        /// <summary>
        /// Applica effetti hover uniformi a qualsiasi controllo
        /// </summary>
        public static void ApplyUniformHoverEffect(Control control, AnimationManager animationManager = null, float lightFactor = 0.2f)
        {
            if (control == null) return;
            
            var originalColor = control.BackColor;
            var hoverColor = ControlPaint.Light(originalColor, lightFactor);
            var pressedColor = ControlPaint.Dark(originalColor, 0.1f);
            
            control.MouseEnter += (s, e) =>
            {
                if (animationManager != null)
                {
                    animationManager.StartColorTransition(control, control.BackColor, hoverColor, 200);
                }
                else
                {
                    control.BackColor = hoverColor;
                }
                control.Cursor = Cursors.Hand;
            };
            
            control.MouseLeave += (s, e) =>
            {
                if (animationManager != null)
                {
                    animationManager.StartColorTransition(control, control.BackColor, originalColor, 200);
                }
                else
                {
                    control.BackColor = originalColor;
                }
            };
            
            control.MouseDown += (s, e) =>
            {
                control.BackColor = pressedColor;
                
                // Effetto scala al click (micro-interazione)
                if (control is Button && animationManager != null)
                {
                    animationManager.StartScale(control, 1.0, 0.98, 100);
                }
                
                // Ripple effect
                if (animationManager != null)
                {
                    animationManager.StartRipple(control, e.Location);
                }
            };
            
            control.MouseUp += (s, e) =>
            {
                control.BackColor = hoverColor;
                
                // Ripristina dimensione originale
                if (control is Button && animationManager != null)
                {
                    animationManager.StartScale(control, 0.98, 1.0, 100);
                }
            };
        }
        
        /// <summary>
        /// Disegna un bordo arrotondato con gradiente
        /// </summary>
        public static void DrawRoundedBorder(Graphics g, Rectangle rect, Color color, int radius, int borderWidth = 1)
        {
            if (g == null) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            using (var path = GetRoundedRectPath(rect, radius))
            using (var pen = new Pen(color, borderWidth))
            {
                g.DrawPath(pen, path);
            }
        }
        
        /// <summary>
        /// Disegna un rettangolo arrotondato con gradiente
        /// </summary>
        public static void DrawRoundedGradient(Graphics g, Rectangle rect, Color color1, Color color2, int radius)
        {
            if (g == null) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            using (var path = GetRoundedRectPath(rect, radius))
            using (var gradient = CreateSubtleGradient(rect, color1, color2))
            {
                if (gradient != null)
                    g.FillPath(gradient, path);
            }
        }
        
        /// <summary>
        /// Disegna un effetto glow attorno a un rettangolo
        /// </summary>
        public static void DrawGlow(Graphics g, Rectangle rect, Color glowColor, int radius, int glowSize = 3)
        {
            if (g == null) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            using (var path = GetRoundedRectPath(rect, radius))
            {
                for (int i = glowSize; i > 0; i--)
                {
                    var alpha = 20 * (glowSize - i + 1) / glowSize;
                    using (var pen = new Pen(Color.FromArgb(alpha, glowColor), i * 2))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }
        
        /// <summary>
        /// Disegna un indicatore dot per campi obbligatori
        /// </summary>
        public static void DrawRequiredIndicator(Graphics g, Point location, Color color, int size = 6)
        {
            if (g == null) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, location.X, location.Y, size, size);
            }
        }
        
        /// <summary>
        /// Disegna una progress line
        /// </summary>
        public static void DrawProgressLine(Graphics g, Rectangle rect, Color backgroundColor, Color progressColor, float progress)
        {
            if (g == null) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            // Sfondo
            using (var bgBrush = new SolidBrush(backgroundColor))
            {
                g.FillRectangle(bgBrush, rect);
            }
            
            // Progress
            if (progress > 0)
            {
                var progressWidth = (int)(rect.Width * Math.Min(progress, 1.0f));
                var progressRect = new Rectangle(rect.X, rect.Y, progressWidth, rect.Height);
                
                using (var progressGradient = new LinearGradientBrush(
                    progressRect,
                    progressColor,
                    ControlPaint.Light(progressColor, 0.3f),
                    LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(progressGradient, progressRect);
                }
            }
        }
        
        /// <summary>
        /// Disegna un effetto ripple
        /// </summary>
        public static void DrawRipple(Graphics g, Rectangle bounds, Point center, double radius, Color color)
        {
            if (g == null || radius <= 0) return;
            
            g.SmoothingMode = SmoothingMode.AntiAlias;
            
            var alpha = (int)(255 * (1 - radius / Math.Max(bounds.Width, bounds.Height)));
            alpha = Math.Max(0, Math.Min(255, alpha));
            
            using (var brush = new SolidBrush(Color.FromArgb(alpha / 2, color)))
            {
                var rippleRect = new RectangleF(
                    (float)(center.X - radius),
                    (float)(center.Y - radius),
                    (float)(radius * 2),
                    (float)(radius * 2)
                );
                
                g.FillEllipse(brush, rippleRect);
            }
        }
        
        /// <summary>
        /// Disegna testo con ombra per profondità
        /// </summary>
        public static void DrawTextWithShadow(Graphics g, string text, Font font, Rectangle bounds, Color textColor, Color shadowColor, TextFormatFlags flags)
        {
            if (g == null || string.IsNullOrEmpty(text)) return;
            
            // Ombra
            var shadowBounds = new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height);
            TextRenderer.DrawText(g, text, font, shadowBounds, shadowColor, flags);
            
            // Testo principale
            TextRenderer.DrawText(g, text, font, bounds, textColor, flags);
        }
        
        /// <summary>
        /// Calcola spaziatura consistente (multipli di 4px)
        /// </summary>
        public static class Spacing
        {
            public const int XXS = 4;
            public const int XS = 8;
            public const int SM = 12;
            public const int MD = 16;
            public const int LG = 24;
            public const int XL = 32;
            public const int XXL = 48;
            
            /// <summary>
            /// Calcola lo stato per fornirlo agli altri step.
            /// </summary>
            public static int Calculate(int multiplier)
            {
                return multiplier * 4;
            }
        }
        
        /// <summary>
        /// Helper per typography hierarchy
        /// </summary>
        public static class Typography
        {
            /// <summary>
            /// Restituisce heading 1 gia pronto.
            /// </summary>
            public static Font GetHeading1(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 18f, FontStyle.Bold);
            }
            
            /// <summary>
            /// Restituisce heading 2 gia pronto.
            /// </summary>
            public static Font GetHeading2(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 14f, FontStyle.Bold);
            }
            
            /// <summary>
            /// Restituisce heading 3 gia pronto.
            /// </summary>
            public static Font GetHeading3(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 12f, FontStyle.Bold);
            }
            
            /// <summary>
            /// Restituisce body gia pronto.
            /// </summary>
            public static Font GetBody(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 10f, FontStyle.Regular);
            }
            
            /// <summary>
            /// Restituisce caption gia pronto.
            /// </summary>
            public static Font GetCaption(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 9f, FontStyle.Regular);
            }
            
            /// <summary>
            /// Restituisce button gia pronto.
            /// </summary>
            public static Font GetButton(string fontFamily = "Segoe UI")
            {
                return new Font(fontFamily, 10f, FontStyle.Bold);
            }
        }
    }
}
