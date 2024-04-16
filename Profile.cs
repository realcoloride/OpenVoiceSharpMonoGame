using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using OpenVoiceSharp;
using Steamworks;
using Steamworks.Data;
using System;
using System.Threading.Tasks;

namespace OpenVoiceSharpMonoGame
{
    public class Profile : IDisposable
    {
        public Friend SteamMember;
        public Texture2D AvatarTexture = null;
        public DynamicSoundEffectInstance SoundEffectInstance = new(VoiceChatInterface.SampleRate, AudioChannels.Stereo);

        public async Task LoadAvatar()
        {
            Image? avatarImage = await SteamMember.GetMediumAvatarAsync();
            if (avatarImage == null) return;

            AvatarTexture = new(
                Game1.Instance.GraphicsDeviceManager.GraphicsDevice, 
                (int)avatarImage?.Width, 
                (int)avatarImage?.Height, 
                true, 
                SurfaceFormat.Color
            );

            AvatarTexture.SetData(avatarImage?.Data);
        }

        public Rectangle AvatarRectangle = new();
        public void Draw(SpriteBatch spriteBatch, int offsetY)
        {
            if (AvatarTexture == null) return;

            string name = SteamMember.Name;
            Vector2 textSize = Game1.Instance.ArialFont.MeasureString(name);

            float avatarWidth = AvatarTexture.Width;
            float avatarHeight = AvatarTexture.Height;

            int avatarPositionX = Game1.WindowWidth - (int)(avatarWidth * 1.25f);
            int avatarPositionY = offsetY + (int)(avatarHeight * 0.25f);

            AvatarRectangle.X = avatarPositionX;
            AvatarRectangle.Y = avatarPositionY;
            AvatarRectangle.Width = AvatarTexture.Width;
            AvatarRectangle.Height = AvatarTexture.Height;

            spriteBatch.Draw(AvatarTexture, AvatarRectangle, Microsoft.Xna.Framework.Color.White);

            int x = avatarPositionX - (int)textSize.X - (int)(avatarWidth * 0.25f);
            int y = avatarPositionY + (int)(avatarHeight / 2.0f) - (int)(textSize.Y / 2.0f);

            spriteBatch.DrawString(
                Game1.Instance.ArialFont, 
                name, 
                new Vector2(x, y), 
                Microsoft.Xna.Framework.Color.White
            );
        }

        public void Dispose()
        {
            SoundEffectInstance.Dispose();
        }

        public Profile(Friend member) {
            SteamMember = member;

            SoundEffectInstance.Volume = 1.0f;
            SoundEffectInstance.Play();
        }
    }
}
