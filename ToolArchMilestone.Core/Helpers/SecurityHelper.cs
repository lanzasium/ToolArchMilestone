using System;
using System.Security.Cryptography;

namespace ToolArchMilestone.Core.Helpers
{
    public static class SecurityHelper
    {
        public static string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%&*";
            
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] randomBytes = new byte[length];
                rng.GetBytes(randomBytes);
                
                char[] password = new char[length];
                for (int i = 0; i < length; i++)
                {
                    password[i] = chars[randomBytes[i] % chars.Length];
                }
                
                return new string(password);
            }
        }
    }
}
