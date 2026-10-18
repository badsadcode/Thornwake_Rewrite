using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// Bot players for the real multiplayer test (GAMEPLAY_DESIGN.md §14 step 2, §14.a; D138): a joined game started with
/// <c>--bots=N</c> opens N more connections to the same host, each a player of its own that walks, hunts with the bow
/// and builds small log huts (and later takes the oldest down) through the same messages a real player's PC sends.
/// They use the world this PC already built (ground and water) and draw nothing; one PC stands in for many players.
/// <c>--bot-spread=m</c>: 0 keeps them together around the host's start (one active area), a distance puts each in a
/// place of its own that far out (twelve areas for the host). Every 10 s a BOTS line goes into the network log:
/// bytes in and out per bot, what arrived, actions done and refused (and why), how long the host took to answer,
/// how regularly the others' positions came, and how many objects each bot knows (they must all agree).
/// </summary>
public sealed class NetBots
{
    private readonly List<Bot> _bots = new();
    private readonly Action<string> _log;

    public IReadOnlyList<Bot> Bots => _bots;

    public NetBots(int count, double spread, string address, int port, string name, string version, Catalog catalog,
        Func<double, double, double> ground, Func<double, double, bool> water, Action<string> log)
    {
        _log = log;
        for (int i = 0; i < count; i++)
            _bots.Add(new Bot(i, count, spread, $"{name}-bot{i + 1}", catalog, ground, water, log, address, port, version));
        _log($"bots: {count} connecting to {address}:{port}, spread {spread:0} m");
    }

    /// <summary>Per frame: every bot's connection, thinking and sending; a BOTS line every 10 s.</summary>
    public void Tick(double dt)
    {
        foreach (var b in _bots) b.Tick(dt);
    }

    public int Connected => _bots.Count(b => b.Welcomed);

    /// <summary>One line over the last <paramref name="seconds"/>: the bots together, plus what the carrier's own copy
    /// of the objects holds (<paramref name="carrierObjects"/>) for the sync check.</summary>
    public string Report(double seconds, int? carrierObjects)
    {
        var c = CultureInfo.InvariantCulture;
        var on = _bots.Where(b => b.Welcomed).ToList();
        if (on.Count == 0) return "BOTS none connected yet";
        double inKb = on.Sum(b => b.PeriodIn) / 1024.0 / seconds, outKb = on.Sum(b => b.PeriodOut) / 1024.0 / seconds;
        var kinds = on.SelectMany(b => b.PeriodKinds).GroupBy(k => k.Key).OrderBy(g => g.Key)
            .Select(g => string.Format(c, "{0} {1:0.0} KB/s", g.Key, g.Sum(k => k.Value) / 1024.0 / seconds / on.Count));
        var rtts = on.SelectMany(b => b.PeriodRtt).ToList();
        var fails = on.SelectMany(b => b.PeriodFails).GroupBy(f => f).OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Count()}× \"{g.Key}\"");
        var gaps = on.Where(b => b.PeriodPositions > 0).ToList();
        var objects = on.Select(b => b.Seen.All.Count).ToList();
        var places = on.Select(b => (b.X, b.Z)).ToList();
        double span = places.Max(p => places.Max(q => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z))));
        string line = string.Format(c, "BOTS {0}/{1} in | per bot in {2:0.0} KB/s out {3:0.00} KB/s (all bots in {4:0.0} out {5:0.0}) | {6}"
            + " | actions {7} ok {8} refused {9}{10} | answer {11:0} ms (max {12:0}) | others' positions every {13:0} ms (max gap {14:0}) | animals seen {15:0.0} per bot"
            + " | objects known {16}–{17}{18} | spread {19:0} m",
            on.Count, _bots.Count, inKb / on.Count, outKb / on.Count, inKb, outKb, string.Join(", ", kinds),
            on.Sum(b => b.PeriodActions), on.Sum(b => b.PeriodOk), on.Sum(b => b.PeriodFails.Count), fails.Any() ? " (" + string.Join(", ", fails) + ")" : "",
            rtts.Count > 0 ? rtts.Average() : 0, rtts.Count > 0 ? rtts.Max() : 0,
            gaps.Count > 0 ? gaps.Average(b => b.PeriodGapSum / b.PeriodPositions * 1000) : 0, gaps.Count > 0 ? gaps.Max(b => b.PeriodGapMax * 1000) : 0,
            on.Average(b => b.Animals.Count), objects.Min(), objects.Max(), carrierObjects is { } co ? $" (this PC {co})" : "", span);
        foreach (var b in _bots) b.ResetPeriod();
        return line;
    }

    public void Close() { foreach (var b in _bots) b.Close(); }

    /// <summary>
    /// One bot: its own connection (a SceneMultiplayer polled by hand, not the scene tree's) and client part, and a
    /// simple mind — wander near home, hunt what it sees for a while, then build a hut, again and again.
    /// </summary>
    public sealed class Bot : IClientGame
    {
        private readonly int _index;
        private readonly string _name, _version;
        public string Name => _name;
        private readonly Catalog _catalog;
        private readonly Func<double, double, double> _ground;
        private readonly Func<double, double, bool> _water;
        private readonly Action<string> _log;
        private readonly double _spread;
        private readonly int _count;
        private readonly SceneMultiplayer _mp = new() { AllowObjectDecoding = false };
        private ENetMultiplayerPeer? _peer;
        private readonly NetClient _client;
        private readonly Random _rng;
        private readonly Inventory _inv;
        private readonly Queue<(ActionKind Kind, double Sent)> _waiting = new();
        private double _now, _stateTimer, _lastPositions = double.NaN;
        private bool _placed;

        public double X, Y, Z;
        private double _vx, _vz, _facing, _homeX, _homeZ, _targetX, _targetZ, _targetTimer;
        public bool Welcomed => _client.Welcomed && _placed;
        public PlacedObjects Seen { get; } = new();
        public List<Animal> Animals { get; private set; } = new();

        // this period's numbers (NetBots.Report)
        public long PeriodIn, PeriodOut;
        public readonly Dictionary<Msg, long> PeriodKinds = new();
        public readonly List<double> PeriodRtt = new();
        public readonly List<string> PeriodFails = new();
        public int PeriodActions, PeriodOk, PeriodPositions;
        public double PeriodGapSum, PeriodGapMax;

        public Bot(int index, int count, double spread, string name, Catalog catalog, Func<double, double, double> ground,
            Func<double, double, bool> water, Action<string> log, string address, int port, string version)
        {
            _index = index; _count = count; _spread = spread; _name = name; _version = version;
            _catalog = catalog; _ground = ground; _water = water; _log = log;
            _rng = new Random(1000 + index);
            _inv = new Inventory(catalog);
            _client = new NetClient(this, (data, reliable) => Send(data, reliable), isHost: false);
            _peer = new ENetMultiplayerPeer();
            if (_peer.CreateClient(address, port) != Error.Ok) { _log($"{_name}: could not connect"); _peer = null; return; }
            _mp.RootPath = "/root"; // (only raw bytes go through it, but it wants a root)
            _mp.MultiplayerPeer = _peer;
            _mp.ConnectedToServer += () => _client.Hello(_name, _version);
            _mp.ServerDisconnected += () => _log($"{_name}: the host closed the connection");
            _mp.PeerPacket += (id, packet) =>
            {
                if (packet.Length == 0 || id != NetServer.HostPeer) return;
                PeriodIn += packet.Length + 28; // (with the UDP and IP headers, as the line counts)
                PeriodKinds[(Msg)packet[0]] = PeriodKinds.GetValueOrDefault((Msg)packet[0]) + packet.Length;
                _client.Receive(packet);
            };
            _phase = _index % 2 == 0 ? Phase.Hunt : Phase.Build; // (half start each way)
            _phaseTimer = 20 + _index * 3;
        }

        private void Send(byte[] data, bool reliable)
        {
            if (_peer == null || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
            PeriodOut += data.Length + 28;
            _mp.SendBytes(data, (int)NetServer.HostPeer, reliable ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.Unreliable, reliable ? 0 : 1);
        }

        public void Close() { _peer?.Close(); _mp.MultiplayerPeer = null; }

        public void ResetPeriod()
        {
            PeriodIn = PeriodOut = 0; PeriodKinds.Clear(); PeriodRtt.Clear(); PeriodFails.Clear();
            PeriodActions = PeriodOk = PeriodPositions = 0; PeriodGapSum = PeriodGapMax = 0;
        }

        private void Act(ActionRequest a)
        {
            _waiting.Enqueue((a.Kind, _now));
            PeriodActions++;
            _client.SendAction(a);
        }

        public void Tick(double dt)
        {
            _now += dt; _dt = dt;
            if (_peer == null) return;
            _mp.Poll();
            if (!_client.Welcomed || !_placed) return;
            Think(dt);
            // positions ~20 a second (NetClient.Tick does the same for a real player)
            if ((_stateTimer -= dt) <= 0)
            {
                _stateTimer = Math.Max(_stateTimer + .05, 0);
                if (((IClientGame)this).LocalState() is { } s) { Send(NetMessages.Build(Msg.State, w => w.Write(s)), false); _firstState = Math.Min(_firstState, _now); }
            }
        }

        // ---- the mind
        private enum Phase { Hunt, Build }
        private Phase _phase;
        private double _phaseTimer, _actTimer;
        private readonly Queue<Action> _plan = new();
        private readonly List<List<long>> _huts = new();
        private readonly List<long> _hut = new();
        private Pose _base;
        private V3 _site;
        private bool _gave;
        private double _firstState = double.PositiveInfinity;

        private void Think(double dt)
        {
            if (!_gave && _now > _firstState + 1) { _gave = true; Act(new ActionRequest(ActionKind.Give, 0, 0, "materials", 0, 0, 0, 0)); Act(new ActionRequest(ActionKind.Give, 0, 0, "tools", 0, 0, 0, 0)); }
            if ((_phaseTimer -= dt) <= 0 && _plan.Count == 0)
            {
                _phase = _phase == Phase.Hunt ? Phase.Build : Phase.Hunt;
                _phaseTimer = _phase == Phase.Hunt ? 50 : 40;
                if (_phase == Phase.Build) StartHut();
            }
            double speed = 1.6;
            if (_phase == Phase.Hunt) speed = Hunt();
            else if (_plan.Count > 0 && (_actTimer -= dt) <= 0) { _actTimer = .5; _plan.Dequeue()(); }
            if (_phase == Phase.Build && _plan.Count > 0) speed = 0; // (standing at the site while building)
            Walk(dt, speed);
        }

        private void Walk(double dt, double speed)
        {
            if ((_targetTimer -= dt) <= 0 || Dist(X, Z, _targetX, _targetZ) < 1.5)
            {
                _targetTimer = 8 + _rng.NextDouble() * 8;
                for (int tries = 0; tries < 8; tries++)
                {
                    double a = _rng.NextDouble() * Math.Tau, d = 5 + _rng.NextDouble() * 30;
                    double x = _homeX + Math.Cos(a) * d, z = _homeZ + Math.Sin(a) * d;
                    if (!_water(x, z)) { _targetX = x; _targetZ = z; break; }
                }
            }
            double dx = _targetX - X, dz = _targetZ - Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < .01 || speed <= 0) { _vx = _vz = 0; return; }
            _vx = dx / len * speed; _vz = dz / len * speed;
            double nx = X + _vx * dt, nz = Z + _vz * dt;
            if (_water(nx, nz)) { _targetTimer = 0; _vx = _vz = 0; return; }
            X = nx; Z = nz; Y = _ground(X, Z);
            _facing = Math.Atan2(_vx, _vz);
        }

        /// <summary>Hunting: go for the nearest living animal seen, shoot it from up to 30 m, butcher what falls.</summary>
        private double Hunt()
        {
            if (Seen.Near(X, Z, 4).FirstOrDefault(o => o.Kind == "carcass") is { } carcass && (_actTimer -= GetDt()) <= 0)
            {
                _actTimer = 1;
                Act(new ActionRequest(ActionKind.Butcher, carcass.Id, 0, "", 0, 0, 0, 0));
                return 0;
            }
            var target = Animals.Where(a => a.State != AnimalState.Dead && Dist(X, Z, a.X, a.Z) < 90).OrderBy(a => Dist(X, Z, a.X, a.Z)).FirstOrDefault();
            if (Seen.Near(X, Z, 40).FirstOrDefault(o => o.Kind == "carcass") is { } far) { _targetX = far.X; _targetZ = far.Z; _targetTimer = 3; return 3; }
            if (target == null) return 1.6;
            _targetX = target.X; _targetZ = target.Z; _targetTimer = 2;
            double d = Dist(X, Z, target.X, target.Z);
            if (d > 30) return 3.5;
            if ((_actTimer -= GetDt()) <= 0)
            {
                _actTimer = 1.2;
                if (_inv.Count("arrow") <= 0) { Act(new ActionRequest(ActionKind.Give, 0, 0, "tools", 0, 0, 0, 0)); return 0; }
                // the arrow as the others draw it, then the hit as this PC claims it (the host decides)
                double y0 = Y + 1.5, ty = target.Y + target.Def.Height * .6, t = d / 45;
                Send(NetMessages.Build(Msg.Shot, w =>
                {
                    w.Write(X); w.Write(y0); w.Write(Z);
                    w.Write((float)((target.X - X) / t)); w.Write((float)((ty - y0) / t + 4.9 * t)); w.Write((float)((target.Z - Z) / t));
                }), false);
                bool hit = _rng.NextDouble() < .7;
                Act(new ActionRequest(ActionKind.Shoot, hit ? target.Id : 0, 0, "", target.X + (hit ? 0 : 2), target.Y, target.Z + (hit ? 0 : 3), 1));
            }
            return 0;
        }

        private double _dt;
        private double GetDt() => _dt;
        private double _lastArrival = double.NaN;

        /// <summary>A hut: four foundations (2 × 2), eight walls round them (one a doorway), a campfire beside; the
        /// oldest of three huts is taken down first.</summary>
        private void StartHut()
        {
            _plan.Clear();
            // (the oldest of three huts taken down — from beside it: the bot goes back there first)
            if (_huts.Count >= 3)
            {
                var old = _huts[0];
                _huts.RemoveAt(0);
                if (old.Select(Seen.Get).FirstOrDefault(o => o != null) is { } first)
                {
                    X = first.X + 4; Z = first.Z + 1; Y = _ground(X, Z); // (a short walk, done at once)
                    foreach (long id in old) _plan.Enqueue(() => { if (Seen.Get(id) != null) Act(new ActionRequest(ActionKind.Dismantle, id, 0, "", 0, 0, 0, 0)); });
                }
            }
            double yaw = _rng.NextDouble() * Math.Tau;
            _site = new V3(X + Math.Sin(yaw) * 5, 0, Z + Math.Cos(yaw) * 5);
            var fdef = _catalog.Pieces["log_foundation"];
            _plan.Enqueue(() =>
            {
                _hut.Clear();
                var pose = Building.Free(fdef, _site with { Y = _ground(_site.X, _site.Z) }, yaw, 0, _ground);
                _base = pose;
                Act(new ActionRequest(ActionKind.Place, 0, 0, fdef.Id, pose.X, pose.Y, pose.Z, (float)pose.Yaw));
            });
            _plan.Enqueue(() => Remember(_base, "log_foundation")); // (the host's copy: its id)
            foreach (var (lx, lz) in new[] { (3.0, 0.0), (0.0, 3.0), (3.0, 3.0) })
                _plan.Enqueue(() => Snapped("log_foundation", lx, 0, lz));
            foreach (var (lx, lz, turn, door) in new[] { (-1.4, 0.0, true, false), (0.0, -1.4, false, false), (4.4, 0.0, true, false), (3.0, -1.4, false, false),
                         (-1.4, 3.0, true, false), (0.0, 4.4, false, true), (4.4, 3.0, true, false), (3.0, 4.4, false, false) })
                _plan.Enqueue(() => Snapped(door ? "log_wall_doorway" : "log_wall", lx, 0, lz, turn ? Math.PI / 2 : 0));
            _plan.Enqueue(() =>
            {
                var at = Building.ToWorld(_base, new V3(-4.5, 0, 1.5));
                Act(new ActionRequest(ActionKind.Place, 0, 0, "campfire", at.X, _ground(at.X, at.Z), at.Z, 0));
            });
            _plan.Enqueue(() =>
            {
                // the hut's pieces as the host sent them back (taken down later)
                var mine = Seen.Near(_base.X, _base.Z, 9).Where(o => _catalog.Piece(o.Kind) != null && !_huts.Any(h => h.Contains(o.Id))).Select(o => o.Id).ToList();
                _huts.Add(mine);
            });
        }

        private void Remember(Pose near, string kind)
        {
            if (Seen.Near(near.X, near.Z, 1).FirstOrDefault(o => o.Kind == kind) is { } o) _base = new Pose(o.X, o.Y, o.Z, o.Yaw);
        }

        private void Snapped(string piece, double lx, double ly, double lz, double turn = 0)
        {
            var def = _catalog.Pieces[piece];
            var aim = Building.ToWorld(_base, new V3(lx, ly, lz));
            var near = GameRules.PiecesNear(_catalog, Seen, aim.X, aim.Z, 12).ToList();
            if (Building.SnapPiece(def, _catalog.Family(def), aim, _base.Yaw + turn, near, new V3(0, 1, 0), _catalog.Smaller(def)) is not { } s)
            {
                PeriodFails.Add($"(bot) {piece} found nothing to snap to");
                return;
            }
            Act(new ActionRequest(ActionKind.Place, 0, 0, s.Piece.Id, s.Pose.X, s.Pose.Y, s.Pose.Z, (float)s.Pose.Yaw));
        }

        private static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        // ---- IClientGame: what the host tells this bot
        void IClientGame.LoadWorld(string worldArgs, string? worldTypeJson, PlayerState? spawn)
        {
            // (the carrier PC built this world already) — home: around the host's start, or a place of its own
            double sx = spawn?.X ?? 0, sz = spawn?.Z ?? 0;
            double angle = _index * Math.Tau / Math.Max(1, _count), r = _spread > 0 ? _spread : 12 + _index % 3 * 6;
            for (int tries = 0; tries < 60; tries++)
            {
                double x = sx + Math.Cos(angle) * r, z = sz + Math.Sin(angle) * r;
                if (!_water(x, z)) { _homeX = x; _homeZ = z; break; }
                angle += .37; if (tries % 12 == 11) r *= .8;
            }
            if (_homeX == 0 && _homeZ == 0) { _homeX = sx + 3; _homeZ = sz + 3; }
            X = _targetX = _homeX; Z = _targetZ = _homeZ; Y = _ground(X, Z);
            _placed = true;
            _log(string.Format(CultureInfo.InvariantCulture, "{0} is in, at {1:0}, {2:0}", _name, X, Z));
        }

        void IClientGame.ApplySettings(IReadOnlyList<(string Key, double Value)> settings) { }
        void IClientGame.ApplyClock(in ClockState clock, bool snap) { }
        void IClientGame.ApplyChunks(IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks) { }
        void IClientGame.PlayerJoined(int id, string name) { }
        void IClientGame.PlayerLeft(int id) { }

        void IClientGame.PlayerMoved(int id, in PlayerState state)
        {
            // (one Players message carries everyone: count the message, not each player in it)
            if (_lastArrival == _now) return;
            _lastArrival = _now;
            if (!double.IsNaN(_lastPositions))
            {
                double gap = _now - _lastPositions;
                PeriodGapSum += gap; PeriodGapMax = Math.Max(PeriodGapMax, gap); PeriodPositions++;
            }
            _lastPositions = _now;
        }

        void IClientGame.DigDone(double volume, double hardness) { }
        void IClientGame.Refused(string reason) => _log($"{_name} refused: {reason}");

        void IClientGame.ActionDone(ActionKind kind, bool ok, string message, double food)
        {
            if (_waiting.Count > 0) { var (_, sent) = _waiting.Dequeue(); PeriodRtt.Add((_now - sent) * 1000); }
            if (ok) PeriodOk++;
            else PeriodFails.Add($"{kind}: {(message.Length > 0 ? message : "(silent)")}");
        }

        void IClientGame.ApplyInventory(BinaryReader r) => _inv.Read(r);

        void IClientGame.ApplyObjects(BinaryReader r)
        {
            for (int n = r.ReadInt32(); n > 0; n--) Seen.Read(r);
        }

        void IClientGame.ApplyRemovals(IReadOnlyList<(int Layer, long X, long Z, double? RemovedAt)> removals) { }
        void IClientGame.TreeHit(in TreeEvent e) { }
        void IClientGame.ObjectMoved(long id, double x, double y, double z, float qx, float qy, float qz, float qw) { }

        PlayerState? IClientGame.LocalState() => _placed
            ? new PlayerState(X, Y, Z, (float)_facing, (float)_vx, 0, (float)_vz, PlayerFlags.None, 3) // (3: the bow in hand)
            : null;

        void IClientGame.ApplyAnimals(BinaryReader r) => Animals = Wildlife.Read(r, _catalog.Animals);
        void IClientGame.Hurt(double damage, double fromX, double fromZ, string who) { }
        void IClientGame.ArrowShot(int player, double x, double y, double z, float vx, float vy, float vz) { }
    }
}
