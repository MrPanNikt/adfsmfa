﻿//******************************************************************************************************************************************************************************************//
// Copyright (c) 2020 Neos-Sdi (http://www.neos-sdi.com)                                                                                                                                    //                        
//                                                                                                                                                                                          //
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),                                       //
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,   //
// and to permit persons to whom the Software is furnished to do so, subject to the following conditions:                                                                                   //
//                                                                                                                                                                                          //
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.                                                           //
//                                                                                                                                                                                          //
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,                                      //
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,                            //
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.                               //
//                                                                                                                                                                                          //
// https://adfsmfa.codeplex.com                                                                                                                                                             //
// https://github.com/neos-sdi/adfsmfa                                                                                                                                                      //
//                                                                                                                                                                                          //
//******************************************************************************************************************************************************************************************//
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Text;
using Neos.IdentityServer.MultiFactor.Data;
using System.Linq;

namespace Neos.IdentityServer.MultiFactor
{
    /// <summary>
    /// BaseEncryption class implmentation
    /// </summary>
    public abstract class BaseEncryption: IDisposable
    {

        /// <summary>
        /// Constructor
        /// </summary>
        public BaseEncryption(string xorsecret)
        {
            XORSecret = xorsecret;
        }

        /// <summary>
        /// XORSecret property
        /// </summary>
        public string XORSecret { get; internal set; } = string.Empty;

        /// <summary>
        /// CheckSum property
        /// </summary>
        public byte[] CheckSum { get; internal set; }

        /// <summary>
        /// Certificate property
        /// </summary>
        public X509Certificate2 Certificate { get; set; } = null;

        public abstract byte[] Encrypt(string username);
        public abstract byte[] Decrypt(byte[] encrypted, string username);
        protected abstract void Dispose(bool disposing);

        /// <summary>
        /// Dispose IDispose method implementation
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Encryption class implmentation
    /// </summary>
    public class Encryption: BaseEncryption
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public Encryption(string xorsecret):base(xorsecret)
        {
            Certificate = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public Encryption(string xorsecret, string thumbprint): base(xorsecret)
        {
            Certificate = Certs.GetCertificate(thumbprint, StoreLocation.LocalMachine);
        }

        /// <summary>
        /// EncryptV1 method (for compatibility with old versions)
        /// </summary>
        public override byte[] Encrypt(string username)
        {
            try
            {
                if (Certificate == null)
                    throw new Exception("Invalid encryption certificate !");
                byte[] plainBytes = GenerateKey(username);
                byte[] encryptedBytes = null;
                var key = Certificate.GetRSAPublicKey();
                if (key == null)
                    throw new CryptographicException("Invalid public Key !");

                if (key is RSACng)
                    encryptedBytes = ((RSACng)key).Encrypt(plainBytes, RSAEncryptionPadding.OaepSHA256);
                else
                    encryptedBytes = ((RSACryptoServiceProvider)key).Encrypt(plainBytes, true);
                return encryptedBytes;
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(Encryption) : Crytographic error for user {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(Encryption) : Encryption error for user {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// Decrypt method
        /// </summary>
        public override byte[] Decrypt(byte[] encryptedBytes, string username)
        {
            try
            {
                if (Certificate == null)
                    throw new Exception("Invalid decryption certificate !");
                byte[] decryptedBytes = null;
                var key = Certificate.GetRSAPrivateKey();
                if (key == null)
                    throw new CryptographicException("Invalid private Key !");

                if (key is RSACng)
                    decryptedBytes = ((RSACng)key).Decrypt(encryptedBytes, RSAEncryptionPadding.OaepSHA256);
                else
                    decryptedBytes = ((RSACryptoServiceProvider)key).Decrypt(encryptedBytes, true);

                MemoryStream mem = new MemoryStream(decryptedBytes);
                string decryptedvalue = DeserializeFromStream(mem);
                int l = Convert.ToInt32(decryptedvalue.Substring(32, 3));

                string outval = decryptedvalue.Substring(35, l);
                byte[] bytes = new byte[outval.Length * sizeof(char)];
                Buffer.BlockCopy(outval.ToCharArray(), 0, bytes, 0, bytes.Length);
                this.CheckSum = CheckSumEncoding.CheckSum(outval); 
                return bytes;
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(Encryption) : Crytographic error for user {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(Encryption) : Decryptionc error for user {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// GenerateKey method (for compatibility with old versions)
        /// </summary>
        private byte[] GenerateKey(string username)
        {
            string ptext = Guid.NewGuid().ToString("N") + username.Length.ToString("000") + username + Guid.NewGuid().ToString("N");
            return SerializeToStream(ptext).ToArray();
        }

        /// <summary>
        /// SerializeToStream
        /// </summary>
        private MemoryStream SerializeToStream(string objectType)
        {
            MemoryStream stream = new MemoryStream();
            IFormatter formatter = new BinaryFormatter();
            formatter.Serialize(stream, objectType);
            return stream;
        }

        /// <summary>
        /// DeserializeFromStream
        /// </summary>
        private string DeserializeFromStream(MemoryStream stream)
        {
            IFormatter formatter = new BinaryFormatter();
            stream.Seek(0, SeekOrigin.Begin);
            object objectType = formatter.Deserialize(stream);
            return (string)objectType;
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Certificate != null)
                    Certificate.Reset();
            }
        }
    }

    /// <summary>
    /// RSAEncryption class implmentation
    /// </summary>
    public class RSAEncryption: BaseEncryption
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public RSAEncryption(string xorsecret): base(xorsecret)
        {
            Certificate = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public RSAEncryption(string xorsecret, string thumbprint): base(xorsecret)
        {
            Certificate = Certs.GetCertificate(thumbprint, StoreLocation.LocalMachine);
        }

        /// <summary>
        /// Encrypt method
        /// </summary>
        public override byte[] Encrypt(string username)
        {
            try
            {
                if (Certificate == null)
                    throw new Exception("Invalid encryption certificate !");
                byte[] plainBytes = GenerateKey(username);
                byte[] encryptedBytes = null;
                var key = Certificate.GetRSAPublicKey();
                if (key == null)
                    throw new CryptographicException("Invalid public Key !");

                if (key is RSACng) 
                    encryptedBytes = ((RSACng)key).Encrypt(plainBytes, RSAEncryptionPadding.OaepSHA256);
                else
                    encryptedBytes = ((RSACryptoServiceProvider)key).Encrypt(plainBytes, true);

                return XORUtilities.XOREncryptOrDecrypt(encryptedBytes, this.XORSecret);
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(RSAEncryption Encrypt) : Crytographic error for user  {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(RSAEncryption Encrypt) : Encryption error for user  {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// Decrypt method
        /// </summary>
        public override byte[] Decrypt(byte[] encryptedBytes, string username)
        {
            try
            {
                if (Certificate == null)
                    throw new Exception("Invalid decryption certificate !");

                byte[] decryptedBytes = XORUtilities.XOREncryptOrDecrypt(encryptedBytes, this.XORSecret);
                byte[] fulldecryptedBytes = null;

                var key = Certificate.GetRSAPrivateKey();
                if (key == null)
                    throw new CryptographicException("Invalid private Key !");

                if (key is RSACng)
                    fulldecryptedBytes = ((RSACng)key).Decrypt(decryptedBytes, RSAEncryptionPadding.OaepSHA256);
                else
                    fulldecryptedBytes = ((RSACryptoServiceProvider)key).Decrypt(decryptedBytes, true);

                byte[] userbuff = new byte[fulldecryptedBytes.Length - 128];
                Buffer.BlockCopy(fulldecryptedBytes, 128, userbuff, 0, fulldecryptedBytes.Length - 128);
                this.CheckSum = userbuff;

                byte[] decryptedkey = new byte[128];
                Buffer.BlockCopy(fulldecryptedBytes, 0, decryptedkey, 0, 128);
                return decryptedkey;
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(RSAEncryption Decrypt) : Crytographic error for user  {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(RSAEncryption Decrypt) : Decryption error for user  {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// GenerateKey method
        /// </summary>
        private byte[] GenerateKey(string username)
        {
            byte[] text = CheckSumEncoding.CheckSum(username);

            byte[] buffer = new byte[128 + text.Length];
            RandomNumberGenerator cryptoRandomDataGenerator = new RNGCryptoServiceProvider();
            cryptoRandomDataGenerator.GetBytes(buffer, 0, 128);
            Buffer.BlockCopy(text, 0, buffer, 128, text.Length);
            return buffer;
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Certificate != null)
                    Certificate.Reset();
            }
        }
    }

    /// <summary>
    /// RNGXOREncryption class implementation
    /// </summary>
    public class RNGEncryption : BaseEncryption
    {
        readonly KeyGeneratorMode _mode = KeyGeneratorMode.ClientSecret128;

        /// <summary>
        /// Constructor
        /// </summary>
        public RNGEncryption(string xorsecret): base(xorsecret)
        {
            _mode = KeyGeneratorMode.ClientSecret128;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public RNGEncryption(string xorsecret, KeyGeneratorMode mode): base(xorsecret)
        {
            _mode = mode;
        }

        /// <summary>
        /// Encrypt method
        /// </summary>
        public override byte[] Encrypt(string username)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                    throw new Exception("Invalid encryption context !");
                byte[] plainBytes = GenerateKey(username);
                return XORUtilities.XOREncryptOrDecrypt(plainBytes, this.XORSecret);
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(RNGEncryption Encrypt) : Crytographic Error for user  {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(RNGEncryption Encrypt) : Encryption error for user  {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// Decrypt method
        /// </summary>
        public override byte[] Decrypt(byte[] encryptedBytes, string username)
        {
            try
            {
                if (encryptedBytes == null)
                    throw new Exception("Invalid decryption context !");

                byte[] decryptedBytes = XORUtilities.XOREncryptOrDecrypt(encryptedBytes, this.XORSecret);
                int size = GetSizeFromMode(_mode);
                byte[] userbuff = new byte[decryptedBytes.Length - size];
                Buffer.BlockCopy(decryptedBytes, size, userbuff, 0, decryptedBytes.Length - size);
                this.CheckSum = userbuff;

                byte[] decryptedkey = new byte[size];
                Buffer.BlockCopy(decryptedBytes, 0, decryptedkey, 0, size);
                return decryptedkey;
            }
            catch (CryptographicException ce)
            {
                Log.WriteEntry(string.Format("(RNGEncryption Decrypt) : Crytographic Error for user {1} \r {0} \r {2}", ce.Message, username, ce.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
            catch (Exception ex)
            {
                Log.WriteEntry(string.Format("(RNGEncryption Decrypt) : Decryption Error for user {1} \r {0} \r {2}", ex.Message, username, ex.StackTrace), System.Diagnostics.EventLogEntryType.Error, 0000);
                return null;
            }
        }

        /// <summary>
        /// GetSizeFromMode method implementation
        /// </summary>
        private int GetSizeFromMode(KeyGeneratorMode xmode)
        {
            switch (_mode)
            {
                case KeyGeneratorMode.ClientSecret128:
                    return 16;
                case KeyGeneratorMode.ClientSecret256:
                    return 32;
                case KeyGeneratorMode.ClientSecret384:
                    return 48;
                case KeyGeneratorMode.ClientSecret512:
                    return 64;
                default:
                    return 16;
            }
        }

        /// <summary>
        /// GenerateKey method
        /// </summary>
        private byte[] GenerateKey(string username)
        {
            byte[] text = CheckSumEncoding.CheckSum(username);

            int size = GetSizeFromMode(_mode);
            byte[] buffer = new byte[size + text.Length];

            RandomNumberGenerator cryptoRandomDataGenerator = new RNGCryptoServiceProvider();
            cryptoRandomDataGenerator.GetBytes(buffer, 0, size);
            Buffer.BlockCopy(text, 0, buffer, size, text.Length);
            return buffer;
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>
    /// AESBaseEncryption class
    /// </summary>
    public abstract class AESBaseEncryption
    {
        public abstract string Encrypt(string data);
        public abstract string Decrypt(string data);
        public abstract byte[] Encrypt(byte[] data);
        public abstract byte[] Decrypt(byte[] data);

        internal abstract byte[] GetHeader(byte[] data);
    }

    /// <summary>
    /// AESEncryption class
    /// </summary>
    public class AESEncryption : AESBaseEncryption, IDisposable
    {
        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override string Encrypt(string data)
        {
            try
            { 
                using (AES256Encryption enc = new AES256Encryption())
                {
                    return enc.Encrypt(data);
                }
            }
            catch
            {
                return data;
            }

        }

        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            try
            {
                using (AES256Encryption enc = new AES256Encryption())
                {
                    return enc.Encrypt(data);
                }
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override string Decrypt(string data)
        {
            try
            {
                byte[] Hdr = GetHeader(Convert.FromBase64String(data));
                if (Hdr.SequenceEqual(new byte[] { 0x17, 0xD3, 0xF4, 0x2A }))
                {
                    using (AES256Encryption enc = new AES256Encryption())
                    {
                        return enc.Decrypt(data);
                    }
                }
                else if (Hdr.SequenceEqual(new byte[] { 0x17, 0xD3, 0xF4, 0x29 })) // For compatibility Only
                {
                    using (AES128Encryption enc = new AES128Encryption())
                    {
                        return enc.Decrypt(data);
                    }
                }
                else
                    return data;
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            try
            {
                byte[] Hdr = GetHeader(data);
                if (Hdr.SequenceEqual(new byte[] { 0x17, 0xD3, 0xF4, 0x2A }))
                {
                    using (AES256Encryption enc = new AES256Encryption())
                    {
                        return enc.Decrypt(data);
                    }
                }
                else if (Hdr.SequenceEqual(new byte[] { 0x17, 0xD3, 0xF4, 0x29 })) // For compatibilty Only
                {
                    using (AES128Encryption enc = new AES128Encryption())
                    {
                        return enc.Decrypt(data);
                    }
                }
                else
                    return data;
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// GetHeader method implementation
        /// </summary>
        internal override byte[] GetHeader(byte[] data)
        {
            byte[] Header = new byte[4];
            Buffer.BlockCopy(data, 0, Header, 0, 4);
            return Header;
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        public void Dispose()
        {

        }
    }


    /// <summary>
    /// AES128Encryption class
    /// </summary>
    internal class AES128Encryption : AESBaseEncryption, IDisposable
    {
        private readonly byte[] IV = { 113, 23, 93, 174, 155, 66, 179, 82, 90, 101, 110, 102, 213, 124, 51, 62 };
        private readonly byte[] Hdr = { 0x17, 0xD3, 0xF4, 0x29 };
        private readonly byte[] AESKey;
        private readonly string UtilsKey = "ABCDEFGHIJKLMNOP";

        /// <summary>
        /// AESEncryption constructor
        /// </summary>
        public AES128Encryption()
        {
            string basestr = UtilsKey;
            AESKey = Encoding.ASCII.GetBytes(basestr.ToCharArray(), 0, 16);
        }

        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override string Encrypt(string data)
        {
            if (string.IsNullOrEmpty(data))
                return data;
            try
            {
                if (IsEncrypted(data))
                   return data;
                byte[] encrypted = EncryptStringToBytes(data, AESKey, IV);
                return Convert.ToBase64String(AddHeader(encrypted));
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override string Decrypt(string data)
        {
            if (string.IsNullOrEmpty(data))
                return data;
            try
            {
                if (!IsEncrypted(data))
                    return data;
                byte[] encrypted = Convert.FromBase64String(data);
                return DecryptStringFromBytes(RemoveHeader(encrypted), AESKey, IV);
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// EncryptStringToBytes method implementation
        /// </summary>
        private byte[] EncryptStringToBytes(string plainText, byte[] Key, byte[] IV)
        {
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");

            byte[] encrypted;
            using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }
            return encrypted;
        }

        /// <summary>
        /// DecryptStringFromBytes method implementation
        /// </summary>
        private string DecryptStringFromBytes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");

            string plaintext = null;
            using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
            return plaintext;
        }

        /// <summary>
        /// IsEncrypted method implementation
        /// </summary>
        private bool IsEncrypted(string data)
        {
            if (string.IsNullOrEmpty(data))
                return false;
            try
            {
                byte[] encrypted = Convert.FromBase64String(data);
                byte[] ProofHeader = GetHeader(encrypted);
                UInt16 l = GetHeaderLen(encrypted);
                return ((l == encrypted.Length - 5) && (ProofHeader.SequenceEqual(Hdr)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// AddHeader method
        /// </summary>
        private byte[] AddHeader(byte[] data)
        {
            byte[] output = new byte[data.Length + 5];
            Buffer.BlockCopy(Hdr, 0, output, 0, 4);
            output[4] = Convert.ToByte(data.Length);
            Buffer.BlockCopy(data, 0, output, 5, data.Length);
            return output;
        }

        /// <summary>
        /// RemoveHeader method
        /// </summary>
        private byte[] RemoveHeader(byte[] data)
        {
            byte[] output = new byte[data.Length - 5];
            Buffer.BlockCopy(data, 5, output, 0, data.Length - 5);
            return output;
        }

        /// <summary>
        /// GetProofHeader method
        /// </summary>
        internal override byte[] GetHeader(byte[] data)
        {
            byte[] Header = new byte[4];
            Buffer.BlockCopy(data, 0, Header, 0, 4);
            return Header;
        }

        /// <summary>
        /// GetLen method
        /// </summary>
        private UInt16 GetHeaderLen(byte[] data)
        { 
            return Convert.ToUInt16(data[4]);
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        public void Dispose()
        {

        }
    }

    /// <summary>
    /// AES256Encryption class
    /// </summary>
    internal class AES256Encryption : AESBaseEncryption, IDisposable
    {
        private readonly byte[] AESIV = { 113, 23, 93, 113, 53, 66, 189, 82, 90, 101, 110, 102, 213, 224, 51, 62 };
        private readonly byte[] AESHdr = { 0x17, 0xD3, 0xF4, 0x2A };
        private readonly byte[] AESKey;

        /// <summary>
        /// AESEncryption constructor
        /// </summary>
        public AES256Encryption()
        {
            byte[] xkey = CFGUtilities.Key;
            AESKey = new byte[32];
            Buffer.BlockCopy(xkey, 0, AESKey, 0, 32);
        }

        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;
            if (IsEncrypted(plainText))
                return plainText;
            try
            {
                byte[] encrypted;
                byte[] unencrypted = Encoding.Unicode.GetBytes(plainText);
                using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
                {
                    aesAlg.BlockSize = 128;
                    aesAlg.KeySize = 256;
                    aesAlg.Key = AESKey;
                    aesAlg.IV = AESIV;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                    {
                        encrypted = encryptor.TransformFinalBlock(unencrypted, 0, unencrypted.Length);
                    }
                }
                return Convert.ToBase64String(AddHeader(encrypted));
            }
            catch
            {
                return plainText;
            }
        }

        /// <summary>
        /// Encrypt method implementation
        /// </summary>
        public override byte[] Encrypt(byte[] unencrypted)
        {
            if (unencrypted == null || unencrypted.Length <= 0)
                throw new ArgumentNullException("unencrypted");
            if (IsEncrypted(unencrypted))
                return unencrypted;
            try
            {
                byte[] encrypted;
                using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
                {
                    aesAlg.BlockSize = 128;
                    aesAlg.KeySize = 256;
                    aesAlg.Key = AESKey;
                    aesAlg.IV = AESIV;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                    {
                        encrypted = encryptor.TransformFinalBlock(unencrypted, 0, unencrypted.Length);
                    }
                }
                return AddHeader(encrypted);
            }
            catch
            {
                return unencrypted;
            }
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;
            if (!IsEncrypted(cipherText))
                return cipherText;
            try
            {
                byte[] encrypted = RemoveHeader(Convert.FromBase64String(cipherText));
                byte[] unencrypted = null;
                using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
                {
                    aesAlg.BlockSize = 128;
                    aesAlg.KeySize = 256;
                    aesAlg.Key = AESKey;
                    aesAlg.IV = AESIV;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    {
                        unencrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                    }
                }
                return Encoding.Unicode.GetString(unencrypted);
            }
            catch
            {
                return cipherText;
            }
        }

        /// <summary>
        /// Decrypt method implementation
        /// </summary>
        public override byte[] Decrypt(byte[] cipherData)
        {
            if (cipherData == null || cipherData.Length <= 0)
                throw new ArgumentNullException("cipherData");
            if (!IsEncrypted(cipherData))
                return cipherData;
            try
            {
                byte[] unencrypted = null;
                byte[] encrypted = RemoveHeader(cipherData);
                using (AesCryptoServiceProvider aesAlg = new AesCryptoServiceProvider())
                {
                    aesAlg.BlockSize = 128;
                    aesAlg.KeySize = 256;
                    aesAlg.Key = AESKey;
                    aesAlg.IV = AESIV;
                    aesAlg.Mode = CipherMode.CBC;
                    aesAlg.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    {
                        unencrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                    }
                }
                return unencrypted;
            }
            catch
            {
                return cipherData;
            }
        }

        /// <summary>
        /// IsEncrypted method implementation
        /// </summary>
        private bool IsEncrypted(string data)
        {
            if (string.IsNullOrEmpty(data))
                return false;
            try
            {
                byte[] encrypted = Convert.FromBase64String(data);
                byte[] ProofHeader = GetHeader(encrypted);
                return ProofHeader.SequenceEqual(AESHdr);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// IsEncrypted method implementation
        /// </summary>
        private bool IsEncrypted(byte[] encrypted)
        {
            try
            {
                byte[] ProofHeader = GetHeader(encrypted);
                return ProofHeader.SequenceEqual(AESHdr);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// AddHeader method
        /// </summary>
        private byte[] AddHeader(byte[] data)
        {
            byte[] output = new byte[data.Length + 4];
            Buffer.BlockCopy(AESHdr, 0, output, 0, 4);
            Buffer.BlockCopy(data, 0, output, 4, data.Length);
            return output;
        }

        /// <summary>
        /// RemoveHeader method
        /// </summary>
        private byte[] RemoveHeader(byte[] data)
        {
            byte[] output = new byte[data.Length - 4];
            Buffer.BlockCopy(data, 4, output, 0, data.Length - 4);
            return output;
        }

        /// <summary>
        /// GetHeader method
        /// </summary>
        internal override byte[] GetHeader(byte[] data)
        {
            byte[] Header = new byte[4];
            Buffer.BlockCopy(data, 0, Header, 0, 4);
            return Header;
        }

        /// <summary>
        /// Dispose method implementation
        /// </summary>
        public void Dispose()
        {

        }
    }

}