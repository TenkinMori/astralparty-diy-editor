using System;
using System.Collections.Generic;
using System.Text;

namespace DiyEditor
{
    /// <summary>撤销栈里的一步：视图（缩放 / 平移）或一次编辑动作。</summary>
    public sealed class UndoStep
    {
        public double Scale, Ox, Oy;
        /// <summary>动手之前那份文档的字节（编辑动作才有）。</summary>
        public byte[] DocText;
        public string Label = "";
        public bool IsView;

        public string Debug
        {
            get
            {
                return (IsView ? "视图" : "编辑") + "「" + Label + "」"
                     + (IsView ? (" scale=" + Scale) : (" 字节 " + (DocText == null ? 0 : DocText.Length)));
            }
        }
    }

    /// <summary>视图的读 / 写（撤销栈不依赖具体页面，自测也能用假的）。</summary>
    public sealed class ViewLink
    {
        public Func<double[]> Get;
        public Action<double[]> Set;

        public double[] Read() { return Get != null ? Get() : new double[3]; }
        public void Write(double[] v) { if (Set != null && v != null && v.Length >= 3) Set(v); }
    }

    /// <summary>撤销 / 重做。编辑动作存"动手之前整棵树的字节"，撤销时重建整棵树。</summary>
    public sealed class UndoStack
    {
        public const int Max = 60;

        private readonly List<UndoStep> _undo = new List<UndoStep>();
        private readonly List<UndoStep> _redo = new List<UndoStep>();

        public int UndoCount { get { return _undo.Count; } }
        public int RedoCount { get { return _redo.Count; } }
        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public void Clear() { _undo.Clear(); _redo.Clear(); }

        public string NextUndoLabel { get { return _undo.Count == 0 ? "" : _undo[_undo.Count - 1].Label; } }
        public string NextRedoLabel { get { return _redo.Count == 0 ? "" : _redo[_redo.Count - 1].Label; } }

        /// <summary>动手之前调用。返回 false = 记不了（没文档）。</summary>
        public bool Capture(LevelDoc doc, string label)
        {
            if (doc == null || doc.Root == null) return false;
            byte[] bytes;
            try { bytes = doc.Bytes(); }
            catch (Exception) { return false; }
            _undo.Add(new UndoStep { DocText = bytes, Label = label, IsView = false });
            Trim();
            _redo.Clear();
            return true;
        }

        public void CaptureView(ViewLink link, string label)
        {
            double[] v = link == null ? new double[3] : link.Read();
            _undo.Add(new UndoStep { IsView = true, Scale = v[0], Ox = v[1], Oy = v[2], Label = label });
            Trim();
            _redo.Clear();
        }

        /// <summary>状态已经拿在手上时（"拖拽开始前"记的那一份）。</summary>
        public void CaptureView(UndoStep view, string label)
        {
            if (view == null) return;
            _undo.Add(new UndoStep
            {
                IsView = true,
                Scale = view.Scale,
                Ox = view.Ox,
                Oy = view.Oy,
                Label = label == null ? view.Label : label,
            });
            Trim();
            _redo.Clear();
        }

        private void Trim() { while (_undo.Count > Max) _undo.RemoveAt(0); }

        /// <summary>撤一步。0 = 没得撤；1 = 撤的是视图（已写回 link）；2 = 撤的是编辑动作（树已回滚）。</summary>
        public int Undo(LevelDoc doc, ViewLink link, out string what)
        {
            what = "";
            if (_undo.Count == 0) return 0;
            UndoStep step = Pop(_undo);
            what = step.Label;

            if (step.IsView)
            {
                double[] now = link == null ? new double[3] : link.Read();
                _redo.Add(new UndoStep { IsView = true, Scale = now[0], Ox = now[1], Oy = now[2], Label = step.Label });
                double[] want = new double[] { step.Scale, step.Ox, step.Oy };
                if (link != null) link.Write(want);
                return 1;
            }

            _redo.Add(new UndoStep { DocText = SafeBytes(doc), Label = step.Label, IsView = false });
            Restore(doc, step.DocText);
            return 2;
        }

        public int Redo(LevelDoc doc, ViewLink link, out string what)
        {
            what = "";
            if (_redo.Count == 0) return 0;
            UndoStep step = Pop(_redo);
            what = step.Label;

            if (step.IsView)
            {
                double[] now = link == null ? new double[3] : link.Read();
                _undo.Add(new UndoStep { IsView = true, Scale = now[0], Ox = now[1], Oy = now[2], Label = step.Label });
                double[] want = new double[] { step.Scale, step.Ox, step.Oy };
                if (link != null) link.Write(want);
                return 1;
            }

            _undo.Add(new UndoStep { DocText = SafeBytes(doc), Label = step.Label, IsView = false });
            Restore(doc, step.DocText);
            return 2;
        }

        private static UndoStep Pop(List<UndoStep> list)
        {
            UndoStep s = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return s;
        }

        private static byte[] SafeBytes(LevelDoc doc)
        {
            if (doc == null || doc.Root == null) return null;
            try { return doc.Bytes(); }
            catch (Exception) { return null; }
        }

        /// <summary>把文档的树换成"那一刻"的样子（内存里，不写盘）。</summary>
        public static void Restore(LevelDoc doc, byte[] bytes)
        {
            if (doc == null || bytes == null) return;
            string text = new UTF8Encoding(false).GetString(bytes);
            doc.Root = DiyJson.Parse(text);
            doc.InvalidateDiy();          // ★ 树换了 ⇒ 怪物池 / 进度的缓存也得跟着重读
            doc.InvalidateStarts();
            doc.InvalidateBoardOverrides();
            BoardStore.ClearCache();
            doc.LoadBoard(null);
        }
    }
}
