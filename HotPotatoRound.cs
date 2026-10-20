using System;
using System.Collections.Generic;

namespace Jj2
{
    sealed class HotPotatoPlayer
    {
        public byte Id;
        public int X, Y;
        public bool HasPosition;
        public double PositionTime;
    }

    enum HotPotatoChangeKind { Holder, Eliminated, Winner, Empty }

    sealed class HotPotatoChange
    {
        public HotPotatoChangeKind Kind;
        public byte? Player, Previous;
    }

    // All time is explicit monotonic seconds; this class has no networking or timers.
    sealed class HotPotatoRound
    {
        public const double FuseSeconds = 15, CooldownSeconds = 1, PositionMaxAge = 1;
        public const int PassRadius = 32;
        readonly Func<int, int> choose;
        readonly HashSet<byte> alive = new HashSet<byte>();
        readonly HashSet<int> contacts = new HashSet<int>();
        double lastPass;
        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        public byte? Holder { get; private set; }
        public byte? Winner { get; private set; }
        public double Deadline { get; private set; }
        public int RemainingPlayers { get { return alive.Count; } }
        public bool Contains(byte id) { return alive.Contains(id); }

        public HotPotatoRound(Func<int, int> choose)
        {
            if (choose == null) throw new ArgumentNullException("choose");
            this.choose = choose;
        }

        public void Reset()
        {
            Running = Finished = false;
            Holder = Winner = null;
            alive.Clear(); contacts.Clear();
            Deadline = lastPass = 0;
        }

        public List<HotPotatoChange> Start(double now, IList<HotPotatoPlayer> players)
        {
            if (Running) throw new InvalidOperationException("A round is already running. Use /reset first.");
            HashSet<byte> ids = new HashSet<byte>();
            foreach (HotPotatoPlayer player in players) ids.Add(player.Id);
            if (ids.Count < 2) throw new InvalidOperationException("Hot Potato needs at least two active non-spectator players.");
            Reset();
            foreach (byte id in ids) alive.Add(id);
            Running = true;
            List<HotPotatoChange> changes = new List<HotPotatoChange>();
            Pick(now, changes);
            return changes;
        }

        static int Pair(byte a, byte b) { return Math.Min(a, b) * 32 + Math.Max(a, b); }
        static bool Fresh(HotPotatoPlayer p, double now)
        {
            return p.HasPosition && now >= p.PositionTime && now - p.PositionTime <= PositionMaxAge;
        }
        static long Distance(HotPotatoPlayer a, HotPotatoPlayer b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        void SetHolder(byte? next, List<HotPotatoChange> changes)
        {
            byte? old = Holder;
            Holder = next;
            if (old != next)
                changes.Add(new HotPotatoChange { Kind = HotPotatoChangeKind.Holder, Player = next, Previous = old });
        }

        void Pick(double now, List<HotPotatoChange> changes)
        {
            List<byte> ids = new List<byte>(alive);
            ids.Sort();
            int selected = choose(ids.Count);
            if (selected < 0 || selected >= ids.Count) throw new InvalidOperationException("Invalid random selection.");
            SetHolder(ids[selected], changes);
            Deadline = now + FuseSeconds;
            lastPass = now;
            contacts.Clear();
        }

        bool Finish(List<HotPotatoChange> changes)
        {
            if (alive.Count > 1) return false;
            SetHolder(null, changes);
            Running = false; Finished = true;
            foreach (byte id in alive) Winner = id;
            changes.Add(new HotPotatoChange { Kind = Winner.HasValue ? HotPotatoChangeKind.Winner : HotPotatoChangeKind.Empty, Player = Winner });
            return true;
        }

        public List<HotPotatoChange> Step(double now, IList<HotPotatoPlayer> players)
        {
            List<HotPotatoChange> changes = new List<HotPotatoChange>();
            if (!Running) return changes;
            Dictionary<byte, HotPotatoPlayer> current = new Dictionary<byte, HotPotatoPlayer>();
            foreach (HotPotatoPlayer player in players) current[player.Id] = player;
            alive.RemoveWhere(delegate(byte id) { return !current.ContainsKey(id); });
            if (Finish(changes)) return changes;
            if (!Holder.HasValue || !alive.Contains(Holder.Value))
            {
                Pick(now, changes);
                return changes;
            }
            if (now >= Deadline)
            {
                byte eliminated = Holder.Value;
                SetHolder(null, changes);
                alive.Remove(eliminated);
                changes.Add(new HotPotatoChange { Kind = HotPotatoChangeKind.Eliminated, Player = eliminated });
                if (!Finish(changes)) Pick(now, changes);
                return changes;
            }

            // A touching pair must separate before passing back, even after cooldown.
            List<int> separated = new List<int>();
            foreach (int pair in contacts)
            {
                byte a = (byte)(pair / 32), b = (byte)(pair % 32);
                if (!alive.Contains(a) || !alive.Contains(b) ||
                    (Fresh(current[a], now) && Fresh(current[b], now) &&
                    Distance(current[a], current[b]) > PassRadius * PassRadius))
                    separated.Add(pair);
            }
            foreach (int pair in separated) contacts.Remove(pair);
            if (now - lastPass < CooldownSeconds) return changes;
            HotPotatoPlayer holder = current[Holder.Value];
            if (!Fresh(holder, now)) return changes;
            List<byte> candidates = new List<byte>(alive);
            candidates.Sort();
            byte? target = null;
            long nearest = (long)PassRadius * PassRadius + 1;
            foreach (byte id in candidates)
            {
                HotPotatoPlayer other = current[id];
                if (id == holder.Id || !Fresh(other, now) || contacts.Contains(Pair(id, holder.Id))) continue;
                long distance = Distance(holder, other);
                if (distance < nearest) { nearest = distance; target = id; }
            }
            if (target.HasValue)
            {
                contacts.Add(Pair(holder.Id, target.Value));
                lastPass = now;
                SetHolder(target, changes);
            }
            return changes;
        }
    }
}
