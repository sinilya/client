using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using MathNet.Numerics.LinearAlgebra;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace ChatClient
{
    public class RC4Cipher
    {
        public byte[] key;
        public int SBlockLength;
        public int[] S;
        public int N;
        public int x, y;

        public RC4Cipher(string _key, int _N)
        {
            this.N = _N;
            this.SBlockLength = (int)Math.Pow(2, N);
            this.S = new int[SBlockLength];
            this.key = ASCIIEncoding.ASCII.GetBytes(_key);

            keySchedulingAlgorithm();
        }

        public void keySchedulingAlgorithm()
        {
            for (int i = 0; i < SBlockLength; i++)
            {
                S[i] = i;
            }

            int j = 0;

            for (int i = 0; i < SBlockLength; i++)
            {
                j = (j + S[i] + key[i % key.Length]) % SBlockLength;
                Swap(i, j);
            }
        }

        public void Swap(int _Si, int _Sj)
        {
            int buffer = S[_Si];
            S[_Si] = S[_Sj];
            S[_Sj] = buffer;
        }

        public string ToBinaryString(Encoding encoding, string text)
        {
            return string.Join("", encoding.GetBytes(text).Select(n => Convert.ToString(n, 2).PadLeft(8, '0')));
        }

        public string ToStringBinary(string data)
        {
            string result = "";

            while (data.Length > 0)
            {
                var first8 = data.Substring(0, 8);
                data = data.Substring(8);
                var number = Convert.ToInt32(first8, 2);
                result += (char)number;
            }
            return result;
        }

        public string xor(string S1, string S2)
        {
            string result = "";

            for (int i = 0; i < N; i++)
            {
                if (S1[i] == S2[i])
                {
                    result += "0";
                }
                else
                {
                    result += "1";
                }
            }
            return result;
        }

        public static string ToBin(int value, int len)
        {
            return (len > 1 ? ToBin(value >> 1, len - 1) : null) + "01"[value & 1];
        }

        public string encode(string text)
        {
            string encodedText = "";
            string binaryText = ToBinaryString(Encoding.GetEncoding(28591), text);
            int binaryTextLength = binaryText.Length, particlesCount = binaryTextLength / N;

            if (binaryTextLength % N != 0)
            {
                binaryText = string.Concat(binaryText, new string('0', (N - (binaryTextLength % N))));
                particlesCount++;
            }

            x = 0; y = 0;

            for (int i = 0; i < particlesCount; i++)
            {
                x = (x + 1) % SBlockLength;
                y = (y + S[x]) % SBlockLength;
                Swap(x, y);

                int t = (S[x] + S[y]) % SBlockLength;
                string k = ToBin(S[t], N);
                string binaryParticle = binaryText.Substring(i * N, N);
                encodedText += xor(k, binaryParticle);
            }

            encodedText = ToStringBinary(encodedText.Substring(0, binaryTextLength));
            keySchedulingAlgorithm();

            return encodedText;
        }
    }
    public struct session
    {
        public string name;
        public RC4Cipher cipher;
    }
    
    class Program
    {
        static string userName;
        private const string host = "127.0.0.1";
        private const int port = 8888;
        static TcpClient client;
        static NetworkStream stream;
        public static int commonSecretKey;
        public static string csk;
        public static List<session> sessionsList = new List<session>();


        static void Main(string[] args)
        {
            Console.Write("Введите свое имя: ");
            while (true)
            {
                userName = Console.ReadLine();
                Regex regex = new Regex(@"\|");
                MatchCollection matches = regex.Matches(userName);
                if (matches.Count == 0)
                {
                    break;
                }
                else
                {
                    Console.WriteLine("Имя не должно содержать символа |");
                }
            }
            client = new TcpClient();
            try
            {
                client.Connect(host, port); 
                stream = client.GetStream(); 

                string message = userName;
                byte[] data = Encoding.Unicode.GetBytes(message);
                stream.Write(data, 0, data.Length);
                
                Thread receiveThread = new Thread(new ThreadStart(ReceiveMessage));
                receiveThread.Start(); 
                Console.WriteLine("Добро пожаловать, {0}", userName);
                handleMessage();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                Disconnect();
            }
        }

        struct clientMessage
        {
            public string type;
            public string data;
        }

        public static session GetSession(string name)
        {
            for(int i = 0; i < sessionsList.Count; i++)
            {
                if(sessionsList[i].name == name)
                {
                    return sessionsList[i];
                }
            }
            return new session() { name = null };
        }

        static void handleMessage()
        {
            while (true)
            {
                clientMessage cm;
                string json;
                string command = Console.ReadLine();
                switch (command)
                {
                    case "help":
                        Console.WriteLine("Список доступных команд:\n1. online - Список пользователей онлайн\n2. connect - установить соединение с " +
                            "пользователем\n4. send - отправка сообщения пользователю\n5. connections - Активные соединения\n6. disconnect - Разорвать " +
                            "соединение с пользователем");
                        break;
                    case "online":
                        cm = new clientMessage() { type = "usersonline" };
                        json = JsonConvert.SerializeObject(cm);
                        sendMessage(json);
                        break;
                    case "connect":
                        Console.WriteLine("Введите имя пользователя, к которому хотите подключиться:");
                        string connectingUser = Console.ReadLine();
                        if(connectingUser == userName)
                        {
                            Console.WriteLine("Зачем вам коннектиться с самим собой? Вы шо, ебобо?");
                            break;
                        }
                        cm = new clientMessage() { type = "connect", data = connectingUser };
                        json = JsonConvert.SerializeObject(cm);
                        sendMessage(json);
                        break;
                    case "send":
                        Console.WriteLine("Введите пользователя, кому хотите отправить сообщение:");
                        string toUser = Console.ReadLine();
                        session sendsession = GetSession(toUser);
                        if(sendsession.name == null)
                        {
                            Console.WriteLine("C таким пользователем у вас не установлена сессия. Отправка сообщений невозможна.");
                            break;
                        }
                        Console.WriteLine("Введите сообщение: ");
                        string message = Console.ReadLine();
                        message = sendsession.cipher.encode(message);
                        Console.WriteLine("Шифрованное сообщение: " + message);
                        cm = new clientMessage() { type = "message|" + toUser, data = message };
                        json = JsonConvert.SerializeObject(cm);
                        sendMessage(json);
                        break;
                    case "connections":
                        cm = new clientMessage() { type = "connections" };
                        json = JsonConvert.SerializeObject(cm);
                        sendMessage(json);
                        break;
                    case "disconnect":
                        Console.WriteLine("С каким пользователем хотите разорвать соединение?");
                        string disconnectUser = Console.ReadLine();
                        cm = new clientMessage() { type = "disconnect", data = disconnectUser };
                        json = JsonConvert.SerializeObject(cm);
                        sendMessage(json);
                        break;
                    default:
                        Console.WriteLine("Неизвестная команда, введите 'help' для получения списка доступных команд.");
                        break;
                }
            }
        }

        static void sendMessage(string message)
        {
            byte[] data = Encoding.Unicode.GetBytes(message);
            stream.Write(data, 0, data.Length);
        }

        public struct serverMessage
        {
            public string type;
            public string data;
            public string name;
        }

        public static void addSession(session ns)
        {
            sessionsList.Add(ns);
        }

        public static void ReceiveMessage()
        {
            while (true)
            {
                try
                {
                    byte[] data = new byte[64];
                    StringBuilder builder = new StringBuilder();
                    int bytes = 0;
                    do
                    {
                        bytes = stream.Read(data, 0, data.Length);
                        builder.Append(Encoding.Unicode.GetString(data, 0, bytes));
                    }
                    while (stream.DataAvailable);

                    string message = builder.ToString();
                    serverMessage sm = JsonConvert.DeserializeObject<serverMessage>(message);
                    switch (sm.type)
                    {
                        case "messageerror":
                            Console.WriteLine("Ошибка, сообщение невозможно отправить: " + sm.data);
                            break;
                        case "usersonline":
                            string[] usersList = sm.data.Split('|');
                            Console.WriteLine("Список пользователей онлайн:");
                            for(int i = 1; i <= usersList.Length; i++)
                            {
                                Console.WriteLine(i.ToString() + ". " + usersList[i-1]);
                            }
                            break;
                        case "rejectconnection":
                            Console.WriteLine("Невозможно установить соединение: " + sm.data);
                            break;
                        case "connections":
                            string[] onlineConnections = sm.data.Split('|');
                            if(onlineConnections[0] == "")
                            {
                                Console.WriteLine("Подключения отсутствуют");
                                break;
                            }
                            Console.WriteLine("Текущие подключения:");
                            for (int i = 0; i < onlineConnections.Length; i++)
                            {
                                Console.WriteLine(i + ". " + onlineConnections[i]);
                            }
                            break;
                        case "alreadydisconnected":
                            Console.WriteLine("Пользователь " + sm.data + " не подключен");
                            break;
                        case "removeconnection":
                            Console.WriteLine("Пользователь " + sm.data + " отключен");
                            break;
                        case "connectionremoved":
                            Console.WriteLine("Пользователь " + sm.data + " отключился");
                            break;
                        case "acceptconnection":
                            Console.WriteLine("Пользователь " + sm.data + " устанавливает соединение...");
                            clientMessage cm = new clientMessage() { type = "accept", data = sm.data };
                            string json = JsonConvert.SerializeObject(cm);
                            Console.WriteLine("JSON :  " + json);
                            sendMessage(json);
                            break;
                        case "accept":
                            string[] keys = sm.data.Split('|');
                            string mySecretKey = keys[1];
                            string companionPublicKey = keys[2];
                            string[] mscdataString = mySecretKey.Split(' ');
                            string[] cpkdataString = companionPublicKey.Split(' ');
                            double[] mscdata = new double[mscdataString.Length];
                            double[] cpkdata = new double[cpkdataString.Length];
                            for(int i = 0; i < mscdataString.Length; i++)
                            {
                                mscdata[i] = Int32.Parse(mscdataString[i]);
                                cpkdata[i] = Int32.Parse(cpkdataString[i]);
                            }
                            Matrix<double> msc = new MathNet.Numerics.LinearAlgebra.Double.DenseMatrix(mscdata.Length, 1, mscdata);
                            Matrix<double> cpk = new MathNet.Numerics.LinearAlgebra.Double.DenseMatrix(cpkdata.Length, 1, cpkdata);
                            //Console.WriteLine("MY SEC: " + msc.ToString());
                            //Console.WriteLine("IT PUB: " + cpk.ToString());

                            msc = msc.Transpose();
                            Matrix<double> sKey = msc.Multiply(cpk);
                            sKey = sKey.Modulus(50);

                            commonSecretKey = (int)sKey[0, 0];
                            //Console.WriteLine("RES: " + Convert.ToString(commonSecretKey));
                            

                            string Digits = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
                            byte[] data123 = BitConverter.GetBytes(commonSecretKey);
                            BigInteger intData = 0;
                            for (int i = 0; i < data123.Length; i++)
                            {
                                intData = intData * 256 + data123[i];
                            }
                            string result = "";
                            while (intData > 0)
                            {
                                int remainder = (int)(intData % 58);
                                intData /= 58;
                                result = Digits[remainder] + result;
                            }
                            for (int i = 0; i < data123.Length && data123[i] == 0; i++)
                            {
                                result = '1' + result;
                            }
                            csk = result;
                            Console.WriteLine("СГЕНЕРИРОВАН СЕКРЕТНЫЙ КЛЮЧ: " + csk);
                            Console.WriteLine("Установлена сессия с пользователем " + sm.name);
                            RC4Cipher newCipher = new RC4Cipher(csk, 19);
                            session newSession = new session() { name = sm.name, cipher = newCipher };

                            addSession(newSession);

                            break;
                        default:
                            string fp = sm.type.Split('|')[0];
                            string sp = sm.type.Split('|')[1];
                            session rs = GetSession(sp);
                            if(fp == "message")
                            {
                                Console.WriteLine(sp + "(шифрованное): " + sm.data);
                                string decodemessage = rs.cipher.encode(sm.data);
                                Console.WriteLine(sp + "(дешифрованное): " + decodemessage);
                            }
                            break;
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine("Подключение прервано! " + e); 
                    Console.ReadLine();
                    Disconnect();
                }
            }
        }

        static void Disconnect()
        {
            if (stream != null)
                stream.Close();
            if (client != null)
                client.Close();
            Environment.Exit(0);
        }
    }
}