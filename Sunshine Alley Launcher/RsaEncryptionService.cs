using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace SunshineAlley_Launcher
{
    public class RsaEncryptionService
    {
        public static int size = 2048;
        public static SecureString SecurePrivateKey;
        public class RsaKeys
        {
            public string PublicKey { get; set; }

            public string PrivateKey { get; set; }
        }

        public static void CreateSecurePrivateKey(string privateKey)
        {
            SecureString securePrivateKey = new SecureString();
            foreach (char c in privateKey)
            {
                securePrivateKey.AppendChar(c);
            }
            //return securePrivateKey;
            SecurePrivateKey = securePrivateKey;
        }
        static void ClearSecurePrivateKey()
        {
            SecurePrivateKey.Dispose();
        }

        public static string OverwriteString(string originalString)
        {
            int length = originalString.Length;
            //char[] randomData = new char[length];
            string randomString = "";
            Random random = new Random();
            for (int i = 0; i < length; i++)
            {
                //randomData[i] = (char)random.Next(32, 127); // Generate random characters in the ASCII range
                //randomString = randomString + randomData[i];
                randomString = randomString + (char)random.Next(32, 127);
            }

            // Overwrite the original string with random data
            //Marshal.Copy(randomData, 0, Marshal.StringToHGlobalAuto(originalString), length);
            return randomString;
        }


        public static RsaKeys GenerateKeys(out RSA rsa)
        {
            //RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(size);
            rsa = new RSACryptoServiceProvider(size);
            RsaKeys keys = new RsaKeys();
            keys.PublicKey = CreateXMLString(rsa, false);
            keys.PrivateKey = CreateXMLString(rsa, true);

            return keys;
        }



        public static string EncryptString(RSA rsa_publicKey, string content) //publicKey
        {
            try
            {
                byte[] bytes;
                bytes = rsa_publicKey.Encrypt(Encoding.UTF8.GetBytes(content), RSAEncryptionPadding.Pkcs1);
                return Convert.ToBase64String(bytes);
            }
            catch
            {
                return null;
            }
        }
        public static string DecryptString(RSA rsa_privateKey, string content)
        {
            try
            {
                byte[] bytes;
                bytes = rsa_privateKey.Decrypt(Convert.FromBase64String(content), RSAEncryptionPadding.Pkcs1);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
        public static List<string> EncryptMaxString(RSA rsa_publicKey, string content)
        {
            List<byte[]> bytes_output = new List<byte[]>();
            List<string> strings = new List<string>();
            try
            {
                byte[] bytes_input = Encoding.UTF8.GetBytes(content);
                //byte[] bytes_output = new byte[0];

                int chunkSize = 245; //fits 2048 rsa size
                int offset = 0;
                while (offset < bytes_input.Length)
                {
                    int remainingBytes = bytes_input.Length - offset;
                    int currentChunkSize = Math.Min(chunkSize, remainingBytes);

                    byte[] chunk = new byte[currentChunkSize];
                    Array.Copy(bytes_input, offset, chunk, 0, currentChunkSize);

                    // Encrypt the chunk and append to output list
                    byte[] bytes = rsa_publicKey.Encrypt(chunk, RSAEncryptionPadding.Pkcs1);
                    strings.Add(Convert.ToBase64String(bytes));
                    bytes_output.Add(bytes);

                    offset += currentChunkSize;
                }
                return strings;
            }
            catch
            {
                return null;
            }
        }
        public static string DecryptMaxString(RSA rsa_privateKey, List<string> content)
        {
            string stringbuilder = "";
            try
            {
                foreach (string str in content)
                {
                    byte[] bytes;
                    bytes = rsa_privateKey.Decrypt(Convert.FromBase64String(str), RSAEncryptionPadding.Pkcs1);
                    stringbuilder += Encoding.UTF8.GetString(bytes);
                }
                return stringbuilder;
            }
            catch
            {
                return null;
            }
        }
        public static string DecryptStringSecureString(string content)
        {
            byte[] bytes;
            string message = "";
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(SecurePrivateKey);
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Marshal.PtrToStringBSTR(ptr));
                    bytes = rsa.Decrypt(Convert.FromBase64String(content), RSAEncryptionPadding.Pkcs1);
                    Marshal.ZeroFreeBSTR(ptr);
                    message = Convert.ToBase64String(bytes);
                }
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
            return message;
        }
        public static string DecryptStringSecureMaxSString(List<string> content)
        {
            string stringbuilder = "";
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(SecurePrivateKey);
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Marshal.PtrToStringBSTR(ptr));

                    foreach (string str in content)
                    {
                        byte[] bytes;
                        bytes = rsa.Decrypt(Convert.FromBase64String(str), RSAEncryptionPadding.Pkcs1);
                        stringbuilder += Encoding.UTF8.GetString(bytes);
                    }
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
            return stringbuilder;
        }




        public static string SignMessage(string message, RSA rsa)
        {
            try
            {
                byte[] dataToSign = Encoding.UTF8.GetBytes(message);
                byte[] signatureBytes = rsa.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return Convert.ToBase64String(signatureBytes);
            }
            catch
            {
                return null;
            }

        }

        public static string SignMessageWithSecureString(string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            return SignMessageWithSecureString(messageBytes);
            /*string signature = "";
            //IntPtr ptr = IntPtr.Zero;
            //try
            //{
            //    ptr = Marshal.SecureStringToBSTR(SecurePrivateKey);
            //    using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            //    {
            //        rsa.FromXmlString(Marshal.PtrToStringBSTR(ptr));
            //        byte[] signatureBytes = rsa.SignData(messageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            //        Marshal.ZeroFreeBSTR(ptr);
            //        signature = Convert.ToBase64String(signatureBytes);
            //    }
            //}
            //finally
            //{
            //    if (ptr != IntPtr.Zero)
            //    {
            //        Marshal.ZeroFreeBSTR(ptr);
            //    }
            //}
            //return signature;
            */
        }
        public static string SignMessageWithSecureString(byte[] messageBytes)
        {
            return Convert.ToBase64String(ByteSignMessageWithSecureString(messageBytes));
            //string signature = "";
            //IntPtr ptr = IntPtr.Zero;
            //try
            //{
            //    ptr = Marshal.SecureStringToBSTR(SecurePrivateKey);
            //    using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            //    {
            //        rsa.FromXmlString(Marshal.PtrToStringBSTR(ptr));
            //        byte[] signatureBytes = rsa.SignData(messageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            //        Marshal.ZeroFreeBSTR(ptr);
            //        signature = Convert.ToBase64String(signatureBytes);
            //    }
            //}
            //finally
            //{
            //    if (ptr != IntPtr.Zero)
            //    {
            //        Marshal.ZeroFreeBSTR(ptr);
            //    }
            //}
            //return signature;
        }
        public static byte[] ByteSignMessageWithSecureString(byte[] messageBytes)
        {
            byte[] signatureBytes;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(SecurePrivateKey);
                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Marshal.PtrToStringBSTR(ptr));
                    signatureBytes = rsa.SignData(messageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(ptr);
                }
            }
            return signatureBytes;
        }

        public static bool VerifySignature(string message, string signature, RSA rsa_publicKey)
        {
            try
            {
                byte[] dataToVerify = Encoding.UTF8.GetBytes(message);
                byte[] signatureByte = Convert.FromBase64String(signature);
                bool isSignatureValid = rsa_publicKey.VerifyData(dataToVerify, signatureByte, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return isSignatureValid;
            }
            catch
            {
                return false;
            }
        }

        public static bool VerifySignature(byte[] dataToVerify, byte[] signatureByte, RSA rsa_publicKey)
        {
            try
            {
                bool isSignatureValid = rsa_publicKey.VerifyData(dataToVerify, signatureByte, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return isSignatureValid;
            }
            catch
            {
                return false;
            }
        }

        private static String CreateXMLString(RSA rsa, bool includePrivateParameters)
        {

            // we extend appropriately for private components
            RSAParameters rsaParams = rsa.ExportParameters(includePrivateParameters);
            StringBuilder sb = new StringBuilder();
            sb.Append("<RSAKeyValue>");
            // Add the modulus
            sb.Append("<Modulus>" + Convert.ToBase64String(rsaParams.Modulus) + "</Modulus>");
            // Add the exponent
            sb.Append("<Exponent>" + Convert.ToBase64String(rsaParams.Exponent) + "</Exponent>");
            if (includePrivateParameters)
            {
                // Add the private components
                sb.Append("<P>" + Convert.ToBase64String(rsaParams.P) + "</P>");
                sb.Append("<Q>" + Convert.ToBase64String(rsaParams.Q) + "</Q>");
                sb.Append("<DP>" + Convert.ToBase64String(rsaParams.DP) + "</DP>");
                sb.Append("<DQ>" + Convert.ToBase64String(rsaParams.DQ) + "</DQ>");
                sb.Append("<InverseQ>" + Convert.ToBase64String(rsaParams.InverseQ) + "</InverseQ>");
                sb.Append("<D>" + Convert.ToBase64String(rsaParams.D) + "</D>");
            }
            sb.Append("</RSAKeyValue>");
            return (sb.ToString());
        }

        public static RSA ParseXMLString(string xmlString)
        {
            try
            {
                RSA rsa = new RSACryptoServiceProvider(size);
                RSAParameters parameters = new RSAParameters();

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlString);

                if (xmlDoc.DocumentElement.Name.Equals("RSAKeyValue"))
                {
                    foreach (XmlNode node in xmlDoc.DocumentElement.ChildNodes)
                    {
                        switch (node.Name)
                        {
                            case "Modulus": parameters.Modulus = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "Exponent": parameters.Exponent = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "P": parameters.P = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "Q": parameters.Q = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "DP": parameters.DP = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "DQ": parameters.DQ = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "InverseQ": parameters.InverseQ = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                            case "D": parameters.D = (string.IsNullOrEmpty(node.InnerText) ? null : Convert.FromBase64String(node.InnerText)); break;
                        }
                    }
                }
                else
                {
                    throw new Exception("Invalid XML RSA key.");
                }

                rsa.ImportParameters(parameters);
                return rsa;
            }
            catch { return null; }

        }

        public static void SaveKeyPairToXml(string publicKeyXml, string privateKeyXml, string filePath)
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml("<RSAKeyValue></RSAKeyValue>");
            xmlDoc.DocumentElement.InnerXml = publicKeyXml;

            if (!string.IsNullOrEmpty(privateKeyXml))
            {
                XmlNode privateKeyNode = xmlDoc.ImportNode(new XmlDocument().CreateCDataSection(privateKeyXml), true);
                xmlDoc.DocumentElement.AppendChild(privateKeyNode);
            }
             
            xmlDoc.Save(filePath);
        }

        public static bool ReadKeyPairFromXml(string filePath, out string publicKeyXml, out string privateKeyXml)
        {
            publicKeyXml = null;
            privateKeyXml = null;

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load(filePath);

                foreach (XmlNode node in xmlDoc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.CDATA)
                    {
                        if (string.IsNullOrEmpty(privateKeyXml))
                            privateKeyXml = node.Value;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(publicKeyXml))
                            publicKeyXml = node.OuterXml;
                    }
                }

                return !string.IsNullOrEmpty(publicKeyXml) && !string.IsNullOrEmpty(privateKeyXml);
            }
            catch (Exception ex)
            {
                //Console.WriteLine("Error reading the XML file: " + ex.Message);
                return false;
            }
        }

        public static byte[] ComputeSHA256Hash(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // Compute the hash
                return sha256.ComputeHash(data);
            }
        }


    }
}
