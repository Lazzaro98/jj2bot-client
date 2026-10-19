using System;

namespace Jj2
{
    /// <summary>
    /// Minimal rabbit physics tuned from captured real-client traffic (70 ticks/s):
    /// run speed 4 px/tick, jump launch -8 px/tick, floor is 20 px below the reported y.
    /// </summary>
    class JjBotBody
    {
        public const int FootOffset = 20;
        private const float RunSpeed = 4f;
        private const float RunAccel = 0.5f;
        private const float JumpSpeed = -8f;
        private const int JumpHoldTicks = 7;
        private const float RiseDecel = 0.65f;
        private const float Gravity = 0.13f;
        private const float MaxFall = 11f;
        private const int MaxStepUp = 8;

        private readonly JjLevelMap map;
        public readonly byte Character;
        private bool prevJump;
        private int jumpTicks;
        private bool doubleJumpUsed;
        private int copterTicks, stompWindup, crouchTicks, kickTicks;
        private bool firing;

        public bool Coptering { get { return copterTicks > 0; } }
        public bool DoubleJumpUsed { get { return doubleJumpUsed; } }
        public bool Sidekicking { get { return kickTicks > 0; } }
        public bool CanFire { get { return !Stomping && !Sidekicking && landTicks == 0; } }

        public float X, Y, VX, VY;
        public bool OnGround;
        public bool FacingRight = true;
        public byte Animation = 61;

        public enum HangType { None, Vine, Hook }

        public HangType Suspended;
        public bool Stomping;

        // Vertical spring launch speeds (px/tick) giving roughly 9, 14 and 19 tiles of lift under plain gravity.
        private static readonly float[] SpringLift = { 8.7f, 10.8f, 12.6f };
        private const float HorizontalSpringSpeed = 12f;
        private const float OverspeedDecay = 0.3f;
        private const float StompAccel = 1.2f;
        private const float HangSpeed = 2.5f;
        private const float ClimbSpeed = 1.5f;
        private const int HandOffset = 20;
        private int springCooldown, hangCooldown, landTicks;
        private bool springFlight;

        public JjBotBody(JjLevelMap map, float x, float y, byte character = 1)
        {
            if (character > 2) throw new ArgumentOutOfRangeException("character");
            this.map = map;
            Character = character;
            X = x;
            Y = y;
        }

        public void ResetPosition(float x, float y)
        {
            X = x;
            Y = y;
            VX = VY = 0;
            OnGround = false;
            Suspended = HangType.None;
            Stomping = springFlight = doubleJumpUsed = prevJump = firing = false;
            copterTicks = stompWindup = crouchTicks = kickTicks = 0;
            springCooldown = hangCooldown = landTicks = jumpTicks = 0;
            Animation = 61;
        }

        private int EventId(int x, int y)
        {
            return map == null ? 0 : map.EventAt(x, y);
        }

        private bool Solid(int x, int y)
        {
            return map == null ? y > 1400 : map.IsSolid(x, y);
        }

        private bool FootBlocked(float x, float y)
        {
            int fy = (int)Math.Round(y) + FootOffset;
            int ix = (int)Math.Round(x);
            return Solid(ix, fy) || Solid(ix - 6, fy) || Solid(ix + 6, fy);
        }

        private bool BodyBlocked(float x, float y, int dir)
        {
            int ix = (int)Math.Round(x) + dir * 10;
            int iy = (int)Math.Round(y);
            for (int yo = -16; yo <= 12; yo += 4)
            {
                if (Solid(ix, iy + yo)) return true;
            }

            return false;
        }

        private bool HeadBlocked(float x, float y)
        {
            int iy = (int)Math.Round(y) - 18;
            int ix = (int)Math.Round(x);
            return Solid(ix, iy) || Solid(ix - 6, iy) || Solid(ix + 6, iy);
        }

        /// <summary>
        /// Advances one 70 Hz tick. dx/dy are -1..1 (dy=1 is down), jump is the held jump button.
        /// </summary>
        public void Step(int dx, int dy, bool jump, bool fire = false)
        {
            if (springCooldown > 0) springCooldown--;
            if (hangCooldown > 0) hangCooldown--;
            if (landTicks > 0) landTicks--;
            if (copterTicks > 0) copterTicks--;
            bool freshJump = jump && !prevJump;
            firing = fire;
            if (OnGround)
            {
                doubleJumpUsed = false;
                copterTicks = 0;
                crouchTicks = dy > 0 ? crouchTicks + 1 : 0;
            }
            else crouchTicks = 0;

            if (Suspended != HangType.None)
            {
                StepSuspended(dx, dy, jump);
                prevJump = jump;
                UpdateAnimation(dx, dy);
                return;
            }

            if (Character == 1 && OnGround && freshJump && dy > 0 &&
                crouchTicks >= 14 && landTicks == 0 && !Sidekicking)
            {
                kickTicks = 39;
                VX = VY = 0;
            }

            if (Sidekicking)
            {
                kickTicks--;
                VX = kickTicks >= 14 ? 0 : (FacingRight ? 12f : -12f);
                MoveHorizontal();
                if (kickTicks < 14 && VX == 0) kickTicks = 0;
                if (!OnGround) VY = Math.Min(MaxFall, VY + Gravity);
                MoveVertical();
                CheckSprings();
                CheckHang();
                prevJump = jump;
                UpdateAnimation(0, 0);
                return;
            }

            if (!OnGround && dy > 0 && !Stomping)
            {
                Stomping = true;
                stompWindup = 39;
                copterTicks = 0;
                springFlight = false;
                VX = 0;
                VY = 0.35f;
            }

            if (Stomping || landTicks > 0 || (OnGround && dy > 0))
            {
                VX = 0;
            }
            else if (Math.Abs(VX) > RunSpeed)
            {
                VX -= Math.Sign(VX) * OverspeedDecay;
                if (Math.Abs(VX) < RunSpeed) VX = Math.Sign(VX) * RunSpeed;
                if (dx != 0) FacingRight = dx > 0;
            }
            else if (dx != 0)
            {
                VX = Math.Max(-RunSpeed, Math.Min(RunSpeed, VX + dx * RunAccel));
                FacingRight = dx > 0;
            }
            else
            {
                VX = Math.Abs(VX) <= RunAccel ? 0 : VX - Math.Sign(VX) * RunAccel;
            }

            MoveHorizontal();

            if (jump && OnGround && landTicks == 0)
            {
                VY = JumpSpeed;
                jumpTicks = 0;
                OnGround = false;
                springFlight = false;
            }
            else if (Character == 1 && freshJump && !OnGround && !doubleJumpUsed && !Stomping)
            {
                doubleJumpUsed = true;
                springFlight = false;
                VY = JumpSpeed;
                jumpTicks = 0;
            }
            else if (Character != 1 && !OnGround && !Stomping && jump && VY >= 0)
            {
                copterTicks = 70;
                springFlight = false;
                VY = Math.Min(VY, 0.6f);
            }

            prevJump = jump;

            if (!OnGround)
            {
                if (Stomping)
                {
                    if (stompWindup > 0)
                    {
                        stompWindup--;
                        VY = 0.35f;
                    }
                    else VY = Math.Min(MaxFall, VY + StompAccel);
                }
                else if (Coptering)
                {
                    VY = Math.Min(0.6f, VY + Gravity);
                }
                else if (VY < 0)
                {
                    if (springFlight) VY += Gravity;
                    else if (jump && jumpTicks < JumpHoldTicks) jumpTicks++;
                    else VY += RiseDecel;
                }
                else
                {
                    VY = Math.Min(MaxFall, VY + Gravity);
                }
            }

            MoveVertical();
            CheckSprings();
            CheckHang();
            UpdateAnimation(dx, dy);
        }

        private void CheckSprings()
        {
            if (map == null || springCooldown > 0) return;
            int ix = (int)Math.Round(X), iy = (int)Math.Round(Y);
            int[] xs = { ix - 8, ix, ix + 8 };
            foreach (int x in xs)
            {
                int ev = EventId(x, iy + FootOffset - 4);
                int id = ev & 0xFF;
                if (id >= 85 && id <= 87 && ((ev >> 12) & 1) == 0 && VY >= 0)
                {
                    VY = -SpringLift[id - 85];
                    LaunchedBySpring();
                    return;
                }

                ev = EventId(x, iy - 18);
                id = ev & 0xFF;
                if (id >= 85 && id <= 87 && ((ev >> 12) & 1) != 0 && VY <= 0)
                {
                    VY = Math.Min(MaxFall, SpringLift[id - 85]);
                    LaunchedBySpring();
                    springFlight = false;
                    return;
                }
            }

            foreach (int x in xs)
            {
                int ev = EventId(x, iy);
                int id = ev & 0xFF;
                if (id < 91 || id > 93) continue;

                int cx = (x >> 5) * 32 + 16, cy = (iy >> 5) * 32 + 16;
                bool wallLeft = Solid(cx - 24, cy), wallRight = Solid(cx + 24, cy);
                int dir = wallLeft && !wallRight ? 1 : wallRight && !wallLeft ? -1 : (((ev >> 19) & 1) != 0 ? -1 : 1);
                if ((ix - cx) * dir < -10) continue;

                VX = dir * HorizontalSpringSpeed;
                FacingRight = dir > 0;
                Stomping = false;
                copterTicks = stompWindup = kickTicks = landTicks = 0;
                springCooldown = 15;
                return;
            }
        }

        private void LaunchedBySpring()
        {
            OnGround = false;
            Stomping = false;
            copterTicks = stompWindup = kickTicks = landTicks = 0;
            doubleJumpUsed = false;
            springFlight = true;
            springCooldown = 15;
        }

        private void CheckHang()
        {
            if (map == null || OnGround || hangCooldown > 0) return;
            int id = EventId((int)Math.Round(X), (int)Math.Round(Y) - HandOffset) & 0xFF;
            if (id != 3 && id != 4) return;

            Suspended = id == 3 ? HangType.Vine : HangType.Hook;
            Stomping = false;
            copterTicks = stompWindup = kickTicks = 0;
            doubleJumpUsed = false;
            springFlight = false;
            VX = 0;
            VY = 0;
        }

        private void Release(int cooldown)
        {
            Suspended = HangType.None;
            hangCooldown = cooldown;
            OnGround = false;
        }

        // Hanging from a vine/hook: no gravity, Down lets go, Jump hops off, Up climbs vines.
        private void StepSuspended(int dx, int dy, bool jump)
        {
            if (dy > 0)
            {
                Release(35);
                VY = 0.5f;
                return;
            }

            if (jump && !prevJump)
            {
                Release(20);
                VY = JumpSpeed;
                jumpTicks = 0;
                return;
            }

            VX = dx * HangSpeed;
            if (dx != 0) FacingRight = dx > 0;
            if (VX != 0 && !BodyBlocked(X + VX, Y, dx))
            {
                X += VX;
            }

            int ix = (int)Math.Round(X), iy = (int)Math.Round(Y);
            if (Suspended == HangType.Vine && dy < 0 && (EventId(ix, iy - HandOffset - 3) & 0xFF) == 3 && !HeadBlocked(X, Y - ClimbSpeed))
            {
                Y -= ClimbSpeed;
            }

            int id = EventId(ix, (int)Math.Round(Y) - HandOffset) & 0xFF;
            if (id != 3 && id != 4)
            {
                Release(10);
                VY = 0;
            }
        }

        private void MoveHorizontal()
        {
            int steps = (int)Math.Ceiling(Math.Abs(VX));
            if (steps == 0) return;
            int dir = Math.Sign(VX);
            float stepSize = VX / steps;
            for (int i = 0; i < steps; i++)
            {
                float nx = X + stepSize;
                if (!BodyBlocked(nx, Y, dir))
                {
                    X = nx;
                    continue;
                }

                bool climbed = false;
                if (OnGround)
                {
                    for (int up = 1; up <= MaxStepUp; up++)
                    {
                        if (!BodyBlocked(nx, Y - up, dir))
                        {
                            X = nx;
                            Y -= up;
                            climbed = true;
                            break;
                        }
                    }
                }

                if (!climbed)
                {
                    VX = 0;
                    return;
                }
            }
        }

        private void MoveVertical()
        {
            if (OnGround)
            {
                // Stick to slopes when walking downhill, drop off ledges otherwise.
                for (int i = 0; i < 4 && !FootBlocked(X, Y + 1); i++) Y += 1;
                if (!FootBlocked(X, Y + 1))
                {
                    OnGround = false;
                    VY = 0;
                }

                return;
            }

            int steps = (int)Math.Ceiling(Math.Abs(VY));
            if (steps == 0)
            {
                if (VY == 0 && FootBlocked(X, Y + 1)) Land();
                return;
            }

            float stepSize = VY / steps;
            for (int i = 0; i < steps; i++)
            {
                float ny = Y + stepSize;
                if (VY > 0)
                {
                    if (FootBlocked(X, ny))
                    {
                        Land();
                        return;
                    }
                }
                else if (HeadBlocked(X, ny))
                {
                    VY = 0;
                    return;
                }

                Y = ny;
            }
        }

        private void Land()
        {
            OnGround = true;
            VY = 0;
            springFlight = doubleJumpUsed = false;
            copterTicks = stompWindup = 0;
            if (Stomping)
            {
                Stomping = false;
                landTicks = 14;
            }
        }

        private void UpdateAnimation(int dx, int dy)
        {
            if (Suspended != HangType.None) Animation = Math.Abs(VX) > 0.3f ? (byte)9 : (byte)21;
            else if (Sidekicking) Animation = kickTicks >= 14 ? (byte)64 : (byte)62;
            else if (Stomping) Animation = stompWindup > 0 ? (byte)60 : (byte)12;
            else if (Coptering) Animation = 25;
            else if (!OnGround) Animation = VY < 0 ? (doubleJumpUsed ? (byte)60 : (byte)39) : (byte)11;
            else if (landTicks > 0) Animation = 2;
            else if (firing && CanFire) Animation = dy > 0 ? (byte)7 : (dy < 0 ? (byte)15 : (byte)14);
            else if (Math.Abs(VX) > 0.4f) Animation = 54;
            else if (dy > 0) Animation = 5;
            else if (dy < 0) Animation = 44;
            else Animation = 61;
        }
    }
}
