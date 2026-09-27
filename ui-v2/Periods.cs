using System;
using System.Collections.Generic;

namespace ALP2
{
    /// <summary>一次周期性分析的结果。</summary>
    internal class PeriodResult
    {
        public double PeriodSec;     // 疑似周期（秒）
        public double Score;         // 相位集中度 0..1，越接近 1 越像"固定间隔"
        public int OnPhase;          // 落在最强相位上的尖峰数
        public int Sample;           // 参与分析的尖峰数
        public int Cycles;           // 时间跨度覆盖了多少个周期

        public string Describe()
        {
            if (PeriodSec <= 0) return "样本不足，看不出周期";
            string unit;
            double v;
            if (PeriodSec >= 120) { v = PeriodSec / 60.0; unit = " 分钟"; }
            else if (PeriodSec >= 1) { v = PeriodSec; unit = " 秒"; }
            else { v = PeriodSec * 1000; unit = " 毫秒"; }
            return string.Format("疑似周期 {0:0.###}{1}（相位集中度 {2:0}%，{3}/{4} 个尖峰落在同一相位，覆盖 {5} 个周期）",
                v, unit, Score * 100, OnPhase, Sample, Cycles);
        }
    }

    /// <summary>
    /// 从尖峰时间序列里找「固定间隔」。
    ///
    /// 这里刻意**不用**相邻间隔的直方图，而是用相位折叠（phase folding）。
    /// 原因：内核 DPC 尖峰通常是「一阵一阵」来的 —— 阵内间隔很小（几百毫秒），
    /// 阵与阵之间很长（十几分钟）。这种形态下相邻间隔的直方图只会得到一大堆小值，
    /// 真正的长周期被埋掉；而且用户看到的"固定间隔"到底是阵内的还是阵间的，
    /// 直方图也回答不了。
    ///
    /// 相位折叠的做法：拿候选周期 P 去除每个时间点，把余数映射到 [0,P)，
    /// 再统计落在各相位段的个数。如果 P 真是周期，所有点会挤在同一个相位上
    /// （集中度接近 1）；如果 P 是真周期的整数倍，点会散到 2、3 个相位上，
    /// 集中度自然掉下来 —— 所以这个方法能自己找到"基本周期"而不会挑到它的倍数。
    /// </summary>
    internal static class Periodicity
    {
        private const int Bins = 24;
        private const int Candidates = 600;

        /// <summary>
        /// timesSec 用同一个单调时钟的秒数（只需相对值）。
        /// 返回 null 表示样本不足以判断。
        /// </summary>
        public static PeriodResult Analyze(IList<double> timesSec, double minPeriod, double maxPeriod)
        {
            if (timesSec == null || timesSec.Count < 6) return null;

            double t0 = double.MaxValue, t1 = double.MinValue;
            for (int i = 0; i < timesSec.Count; i++)
            {
                if (timesSec[i] < t0) t0 = timesSec[i];
                if (timesSec[i] > t1) t1 = timesSec[i];
            }
            double span = t1 - t0;
            if (span <= 0) return null;

            // 至少要能看见 3 个完整周期才敢说"周期"
            double hi = Math.Min(maxPeriod, span / 3.0);
            if (hi < minPeriod) return null;

            int n = timesSec.Count;
            int[] bin = new int[Bins];
            double bestP = 0, bestScore = 0; int bestOn = 0;

            for (int c = 0; c < Candidates; c++)
            {
                // 对数步长：从毫秒级到分钟级都要覆盖
                double frac = c / (double)(Candidates - 1);
                double p = minPeriod * Math.Pow(hi / minPeriod, frac);
                if (p <= 0) continue;

                for (int i = 0; i < Bins; i++) bin[i] = 0;
                for (int i = 0; i < n; i++)
                {
                    double ph = (timesSec[i] - t0) % p;
                    if (ph < 0) ph += p;
                    int b = (int)(ph / p * Bins);
                    if (b >= Bins) b = Bins - 1;
                    bin[b]++;
                }
                int m = 0;
                for (int i = 0; i < Bins; i++) if (bin[i] > m) m = bin[i];

                double score = m / (double)n;
                // 分数更高的胜出；分数接近时取更小的周期（基本周期优先于它的倍数）
                if (score > bestScore + 0.02 || (Math.Abs(score - bestScore) <= 0.02 && p < bestP))
                {
                    bestScore = score; bestP = p; bestOn = m;
                }
            }

            if (bestP <= 0 || bestScore < 0.34) return null;   // 随机分布下 24 格里最高也就两成左右

            PeriodResult r = new PeriodResult();
            r.PeriodSec = bestP;
            r.Score = bestScore;
            r.OnPhase = bestOn;
            r.Sample = n;
            r.Cycles = (int)Math.Floor(span / bestP);
            return r;
        }

        /// <summary>
        /// 相邻间隔的统计（中位数/最小/最大），用来描述"阵内"的疏密。
        /// 单看它看不出长周期，但和上面的相位折叠合起来能把形态说清楚。
        /// </summary>
        public static void IntervalStats(IList<double> timesSec, out double medianMs, out double minMs, out double maxMs)
        {
            medianMs = minMs = maxMs = 0;
            if (timesSec == null || timesSec.Count < 2) return;

            List<double> d = new List<double>(timesSec.Count - 1);
            for (int i = 1; i < timesSec.Count; i++)
            {
                double ms = (timesSec[i] - timesSec[i - 1]) * 1000.0;
                if (ms >= 0) d.Add(ms);
            }
            if (d.Count == 0) return;
            d.Sort();
            minMs = d[0];
            maxMs = d[d.Count - 1];
            medianMs = d[d.Count / 2];
        }
    }
}
