using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DiyEditor
{
    /// <summary>
    /// 磨砂玻璃面板：把"背后的那张背景图"截一块下来高斯模糊当自己的底。
    ///
    /// 原理：拿 Source（窗口最底下那层背景 Grid）做一个 VisualBrush，Viewbox 取
    /// "本面板在窗口里占的那块区域"，Viewport 反过来铺满自己，再给刷子加一层
    /// BlurEffect —— 看起来就是毛玻璃。四周多采样一圈（pad）是为了让模糊有邻居
    /// 像素可用，不然边缘会因为采不到内容而发虚。
    /// </summary>
    public class GlassSurface : ContentControl
    {

        private static double _frost = -1;

        /// <summary>磨砂半径变了 —— 每一块活着的玻璃都立刻跟着重画。</summary>
        public static event Action FrostChanged;

        /// <summary>
        /// 全局磨砂半径：>=0 时压过各自的 BlurRadius（设置里那条「玻璃磨砂」）。
        /// 这样不管面板是写在 XAML 里的、还是从按钮模板里长出来的，糊得都一样厚。
        ///
        /// 写它是**广播**，不是只改一个数字：光设字段的话，那些靠 GlobalFrost 生效、
        /// 自己 BlurRadius 没变的玻璃（按钮模板里那几颗药丸）收不到属性通知，
        /// 要等下一次布局才肯重画 —— 表现就是"拖磨砂滑杆，药丸的底不动"。
        /// </summary>
        public static double GlobalFrost
        {
            get { return _frost; }
            set
            {
                if (Math.Abs(_frost - value) < 0.001)
                    return;
                _frost = value;
                Action handler = FrostChanged;
                if (handler != null)
                    handler();
            }
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register("CornerRadius", typeof(CornerRadius), typeof(GlassSurface),
                new PropertyMetadata(new CornerRadius(0), OnVisualChanged));

        /// <summary>玻璃上的那层薄色（半透明）。</summary>
        public static readonly DependencyProperty TintProperty =
            DependencyProperty.Register("Tint", typeof(Brush), typeof(GlassSurface),
                new PropertyMetadata(null, OnVisualChanged));

        /// <summary>描边。</summary>
        public static readonly DependencyProperty EdgeBrushProperty =
            DependencyProperty.Register("EdgeBrush", typeof(Brush), typeof(GlassSurface),
                new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty EdgeThicknessProperty =
            DependencyProperty.Register("EdgeThickness", typeof(Thickness), typeof(GlassSurface),
                new PropertyMetadata(new Thickness(0), OnVisualChanged));

        /// <summary>高斯模糊半径。</summary>
        public static readonly DependencyProperty BlurRadiusProperty =
            DependencyProperty.Register("BlurRadius", typeof(double), typeof(GlassSurface),
                new PropertyMetadata(28.0, OnVisualChanged));

        /// <summary>要模糊的那层背景（窗口根 Grid 里最底下的那个 Grid）。</summary>
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register("Source", typeof(FrameworkElement), typeof(GlassSurface),
                new PropertyMetadata(null, OnVisualChanged));

        private VisualBrush _brush;
        private string _stamp = "";

        public CornerRadius CornerRadius
        {
            get { return (CornerRadius)GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, value); }
        }

        public Brush Tint
        {
            get { return (Brush)GetValue(TintProperty); }
            set { SetValue(TintProperty, value); }
        }

        public Brush EdgeBrush
        {
            get { return (Brush)GetValue(EdgeBrushProperty); }
            set { SetValue(EdgeBrushProperty, value); }
        }

        public Thickness EdgeThickness
        {
            get { return (Thickness)GetValue(EdgeThicknessProperty); }
            set { SetValue(EdgeThicknessProperty, value); }
        }

        public double BlurRadius
        {
            get { return (double)GetValue(BlurRadiusProperty); }
            set { SetValue(BlurRadiusProperty, value); }
        }

        public FrameworkElement Source
        {
            get { return (FrameworkElement)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        public GlassSurface()
        {
            Loaded += delegate { FrostChanged += OnFrostChanged; Hook(); Apply(); };
            Unloaded += delegate { FrostChanged -= OnFrostChanged; LayoutUpdated -= OnLayoutUpdated; };
            SizeChanged += delegate { Apply(); };
            IsVisibleChanged += delegate { Hook(); Apply(); };
        }

        /// <summary>全局磨砂变了：把缓存戳清掉再画一次，否则 Apply 会因为"没变"直接 return。</summary>
        private void OnFrostChanged()
        {
            _stamp = "";
            Apply();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var glass = d as GlassSurface;
            if (glass != null)
                glass.Apply();
        }

        private void Hook()
        {
            LayoutUpdated -= OnLayoutUpdated;
            if (IsLoaded)
                LayoutUpdated += OnLayoutUpdated;
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            Apply();
        }

        private void Apply()
        {
            var host = GetTemplateChild("PART_Host") as Grid;
            var fill = GetTemplateChild("PART_Fill") as Rectangle;
            if (host == null || fill == null)
                return;

            double width = ActualWidth;
            double height = ActualHeight;
            double radius = CornerRadius.TopLeft;
            if (width <= 0.5 || height <= 0.5)
                return;

            // 半径不可能超过短边的一半，多出来的自己夹一下（改 999 之类的不会画成椭圆）
            radius = Math.Min(radius, Math.Min(width, height) / 2);
            host.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);

            FrameworkElement source = ResolveSource();
            if (source == null || !IsVisible)
            {
                fill.Fill = null;
                fill.Effect = null;
                // 笔刷也要丢掉：它记着旧的 Visual，留着的话下面"笔刷没变就跳过"那一步
                // 会连 Fill 一起跳过 —— 面板藏过一轮再露出来就永远没有磨砂底了
                // （表现就是"切过页面之后，那颗药丸改磨砂没反应"）。
                _brush = null;
                _stamp = "";
                return;
            }

            Point origin;
            if (!TryOrigin(source, out origin))
                return;

            double blur = Math.Max(0, GlobalFrost >= 0 ? GlobalFrost : BlurRadius);
            double pad = blur <= 0.01 ? 0 : Math.Min(blur * 1.8 + 4, 160);

            string stamp = string.Format("{0:0.##}|{1:0.##}|{2:0.##}|{3:0.##}|{4:0.##}",
                                         origin.X, origin.Y, width, height, blur);
            if (stamp == _stamp)
                return;
            _stamp = stamp;

            if (_brush == null || !ReferenceEquals(_brush.Visual, source))
            {
                _brush = new VisualBrush(source)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
            }
            fill.Fill = _brush;      // 每次都确认挂上（上面可能刚把它清掉）
            _brush.ViewboxUnits = BrushMappingMode.Absolute;
            _brush.ViewportUnits = BrushMappingMode.Absolute;
            _brush.Viewbox = new Rect(origin.X - pad, origin.Y - pad, width + pad * 2, height + pad * 2);
            _brush.Viewport = new Rect(-pad, -pad, width + pad * 2, height + pad * 2);

            fill.Effect = blur <= 0.01
                ? null
                : new BlurEffect
                {
                    Radius = blur,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance,
                };
        }

        /// <summary>没显式指定 Source 就自动找窗口里那个叫 Backdrop 的背景层。</summary>
        private FrameworkElement ResolveSource()
        {
            if (Source != null)
                return Source;
            Window window = Window.GetWindow(this);
            if (window == null)
                return null;
            return window.FindName("Backdrop") as FrameworkElement;
        }

        /// <summary>算出本面板左上角在背景层坐标系里的位置。</summary>
        private bool TryOrigin(FrameworkElement source, out Point origin)
        {
            origin = new Point(0, 0);
            try
            {
                Window window = Window.GetWindow(this);
                if (window != null && source.IsDescendantOf(window))
                {
                    Point mine = TransformToAncestor(window).Transform(new Point(0, 0));
                    Point theirs = source.TransformToAncestor(window).Transform(new Point(0, 0));
                    origin = new Point(mine.X - theirs.X, mine.Y - theirs.Y);
                    return true;
                }
                Point inSource = TransformToAncestor(source).Transform(new Point(0, 0));
                origin = new Point(-inSource.X, -inSource.Y);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}

