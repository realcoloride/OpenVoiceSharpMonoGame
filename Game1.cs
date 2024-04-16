using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenVoiceSharp;
using Steamworks;
using Steamworks.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenVoiceSharpMonoGame
{
    public class Game1 : Game
    {
        public static Game1 Instance;

        public GraphicsDeviceManager GraphicsDeviceManager;
        private SpriteBatch SpriteBatch;

        // openvoicesharp
        public VoiceChatInterface VoiceChatInterface = new(enableNoiseSuppression: false, stereo: true);
        public BasicMicrophoneRecorder MicrophoneRecorder = new(true);

        // steam
        public Lobby? Lobby;

        public bool Connected = false;
        private Dictionary<SteamId, Profile> Profiles = new();

        // cooldowns
        public bool BusyCreating = false;
        public bool BusyTogglingNoiseSuppression = false;

        public string Status = "Not connected";
        public string Action = "host";

        public SpriteFont ArialFont;

        public const int WindowWidth = 1280;
        public const int WindowHeight = 720;

        public Game1()
        {
            GraphicsDeviceManager = new(this);

            GraphicsDeviceManager.PreferredBackBufferWidth = WindowWidth;
            GraphicsDeviceManager.PreferredBackBufferHeight = WindowHeight;
            GraphicsDeviceManager.ApplyChanges();

            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            // set instance to use later
            Instance = this;
        }

        // profile
        private Profile GetProfile(SteamId steamId) => Profiles[steamId];
        private async Task CreateProfile(Friend friend)
        {
            // avoid creating clones
            if (Profiles.ContainsKey(friend.Id)) return;

            Profile profile = new(friend);
            await profile.LoadAvatar();

            Profiles.Add(friend.Id, profile);
        }
        public void DeleteProfile(SteamId steamId)
        {
            Profiles.Remove(steamId);
        }

        public async Task SetupLobby()
        {
            foreach (var member in Lobby?.Members)
            {
                // create all previous profiles
                await CreateProfile(member);
            }
        }

        public async Task HostOrLeave()
        {
            // leave/stop hosting
            if (Lobby != null)
            {
                // clear player list and audio playbacks
                foreach (var entry in Profiles)
                {
                    entry.Value.Dispose();
                    Profiles.Remove(entry.Key);
                }

                Lobby?.Leave();
                Lobby = null;
                Connected = false;

                Status = "Not connected";
                Action = "host";

                return;
            }

            Status = "Creating lobby...";
            var createLobbyOutput = await SteamMatchmaking.CreateLobbyAsync(4);
            if (createLobbyOutput == null)
            {
                Status = "Not connected";
                return;
            }

            Lobby = createLobbyOutput.Value;

            Lobby?.SetPublic();
            Lobby?.SetJoinable(true);

            Status = "Joining lobby...";
            await Lobby?.Join();
        }

        protected override void Initialize()
        {
            // steam events
            SteamMatchmaking.OnLobbyMemberJoined += async (lobby, friend) =>
            {
                await CreateProfile(friend);
            };
            SteamMatchmaking.OnLobbyMemberLeave += (lobby, friend) =>
            {
                DeleteProfile(friend.Id);
            };

            SteamMatchmaking.OnLobbyEntered += async (joinedLobby) =>
            {
                Status = "Connected";

                // set to current lobby
                Lobby = joinedLobby;

                // setup
                await SetupLobby();

                Action = "stop hosting";

                Connected = true;
            };
            SteamFriends.OnGameLobbyJoinRequested += async (joinedLobby, friend) =>
            {
                // do not accept any join requests if already in lobby
                if (Lobby != null) return;

                Status = "Accepted invite, joining...";

                // set to current lobby
                Lobby = joinedLobby;

                // setup
                await joinedLobby.Join();

                Action = "leave";
                Connected = true;
            };

            // microphone rec
            MicrophoneRecorder.DataAvailable += (pcmData, length) => {
                // if not connected or not talking, ignore
                if (!Connected) return;
                if (!VoiceChatInterface.IsSpeaking(pcmData)) return;

                // encode the audio data and apply noise suppression.
                (byte[] encodedData, int encodedLength) = VoiceChatInterface.SubmitAudioData(pcmData, length);

                // send packet to everyone (P2P)
                foreach (var member in Lobby?.Members)
                    SteamNetworking.SendP2PPacket(member.Id, encodedData, encodedLength, 0, P2PSend.Reliable);
            };
            MicrophoneRecorder.StartRecording();

            // initialize steam
            SteamClient.Init(480);

            base.Initialize();
        }

        protected override void LoadContent()
        {
            SpriteBatch = new SpriteBatch(GraphicsDevice);
            ArialFont = Content.Load<SpriteFont>("Arial");
            base.LoadContent();
        }

        void HandleMessageFrom(SteamId steamid, byte[] data)
        {
            //if (steamid == SteamClient.SteamId) return;

            // decode data
            (byte[] decodedData, int decodedLength) = VoiceChatInterface.WhenDataReceived(data, data.Length);

            // push to sound effect instance buffer
            GetProfile(steamid).SoundEffectInstance.SubmitBuffer(decodedData, 0, decodedLength);
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            SteamClient.RunCallbacks();

            var keyboardState = Keyboard.GetState();

            if (keyboardState.IsKeyDown(Keys.E) && !BusyCreating)
            {
                Task.Run(async () =>
                {
                    BusyCreating = true;
                    await HostOrLeave();

                    // cooldown
                    await Task.Delay(1000);
                    BusyCreating = false;
                });
            }

            if (keyboardState.IsKeyDown(Keys.F) && !BusyTogglingNoiseSuppression)
            {
                Task.Run(async () =>
                {
                    BusyTogglingNoiseSuppression = true;
                    
                    // invert
                    VoiceChatInterface.EnableNoiseSuppression = !VoiceChatInterface.EnableNoiseSuppression;

                    // cooldown
                    await Task.Delay(700);
                    BusyTogglingNoiseSuppression = false;
                });
            }


            if (Lobby == null) return;

            while (SteamNetworking.IsP2PPacketAvailable())
            {
                var packet = SteamNetworking.ReadP2PPacket();
                if (!packet.HasValue) continue;

                HandleMessageFrom(packet.Value.SteamId, packet.Value.Data);
            }

            base.Update(gameTime);
        }


        public void DrawString(string text, ref Vector2 position)
        {
            SpriteBatch.DrawString(ArialFont, text, position, Microsoft.Xna.Framework.Color.White);
            position.Y += 16;
        }
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.CornflowerBlue);

            Vector2 textPosition = new();
            
            SpriteBatch.Begin();

            DrawString(Status, ref textPosition);
            DrawString("Shift+TAB or use the steam app to invite someone", ref textPosition);
            DrawString($"Press [E] to {Action}", ref textPosition);
            DrawString($"Press [F] to {(VoiceChatInterface.EnableNoiseSuppression ? "disable" : "enable")} noise suppression (RNNoise)", ref textPosition);

            int offsetY = 0;
            foreach (var profile in Profiles.Values)
            {
                profile.Draw(SpriteBatch, ref offsetY);
            }

            SpriteBatch.End();

            base.Draw(gameTime);
        }
    }
}