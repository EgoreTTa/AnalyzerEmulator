namespace AnalyzerEmulator
{
    using System.IO.Ports;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;

    internal enum ConnectionType { Network, COM, File }

    internal enum ProtocolType { HL7, ASTM, JSON }

    internal class Settings
    {
        public ConnectionType Connection { get; set; } = ConnectionType.Network;
        public ProtocolType Protocol { get; set; } = ProtocolType.ASTM;
        public string Target { get; set; } = "127.0.0.1:8080";
        public int TimeoutMs { get; set; } = 5000; //салам салам
    }

    internal class Sample
    {
        public string SampleId { get; set; }
        public string Barcode { get; set; }
        public string TestName { get; set; }
        public string Result { get; set; }
        public string Unit { get; set; }
    }

    internal class Program
    {
        const byte STX = 0x02;
        const byte ETX = 0x03;
        const byte EOT = 0x04;
        const byte ENQ = 0x05;
        const byte ACK = 0x06;
        const byte NAK = 0x15;
        const byte ETB = 0x17;
        
        const byte CR = 0x0D;
        const byte LF = 0x0A;
        
        const byte VT = 0x0B;
        const byte FS = 0x1C;

        private static readonly Settings Settings = new Settings();
        private static readonly Random Rnd = new Random();

        private static void Main()
        {
            ViewMainMenu();
        }

        private static void ViewMainMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"[Настройки: {Settings.Connection} | {Settings.Protocol} | {Settings.Target} | TO:{Settings.TimeoutMs}ms]\n");
                Console.WriteLine("1. Симуляция (отправить 3 образца)");
                Console.WriteLine("2. Настройки");
                Console.WriteLine("3. Выход");
                Console.Write("\nВыбор: ");

                switch (Console.ReadLine())
                {
                    case "1": RunSimulation(); break;
                    case "2": ConfigureSettings(); break;
                    case "3": return;
                }
            }
        }

        private static void ConfigureSettings()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine($"[Настройки: {Settings.Connection} | {Settings.Protocol} | {Settings.Target} | TO:{Settings.TimeoutMs}ms]\n");
                Console.WriteLine("1. Тип подключения (Network, COM, File)");
                Console.WriteLine("2. Протокол (HL7, ASTM, JSON)");
                Console.WriteLine("3. Целевой адрес (IP:Port / COMx / Путь)");
                Console.WriteLine("4. Таймаут ответа (мс)");
                Console.WriteLine("0. Назад");
                Console.Write("\nВыбор: ");

                switch (Console.ReadLine())
                {
                    case "0":
                        return;
                    case "1":
                        Console.Write("Введите 1-Network, 2-COM, 3-File: ");
                        Settings.Connection = (ConnectionType)(int.Parse(Console.ReadLine()) - 1);
                        break;
                    case "2":
                        Console.Write("Введите 1-HL7, 2-ASTM, 3-JSON: ");
                        Settings.Protocol = (ProtocolType)(int.Parse(Console.ReadLine()) - 1);
                        break;
                    case "3":
                        Console.Write("Введите адрес: ");
                        Settings.Target = Console.ReadLine();
                        break;
                    case "4":
                        Console.Write("Таймаут (мс): ");
                        Settings.TimeoutMs = int.Parse(Console.ReadLine());
                        break;
                }
            }
        }

        private static void RunSimulation()
        {
            Console.WriteLine("\n--- Генерация и отправка образцов ---");
            for (var i = 1; i <= 3; i++)
            {
                var sample = new Sample
                {
                    SampleId = $"{DateTime.Now:yyyyMMdd}{Rnd.Next(9999):000000}",
                    TestName = "Glucose",
                    Result = $"{Rnd.Next(200000)/1000.0}",
                    Unit = "mmol/L"
                };

                var payload = FormatData(sample);

                var ok = false;
                try { ok = SendData(payload); }
                catch (Exception exception) { }

                Console.WriteLine(ok ? $"[OK] Образец {sample.SampleId} отправлен."
                                     : $"[FAIL] Образец {sample.SampleId} — ошибка передачи.");
                System.Threading.Thread.Sleep(500);
            }

            Console.WriteLine("\nНажмите любую клавишу для возврата в меню...");
            Console.ReadKey();
        }

        private static string FormatData(Sample sample)
        {
            switch (Settings.Protocol)
            {
                case ProtocolType.HL7:
                    return $"MSH|^~\\&|AnalyzerEmulator|LAB|LIS|HOSP|{DateTime.Now:yyyyMMddHHmmss}||ORU^R01|{Rnd.Next(1000)}|P|2.3.1\r" +
                           $"PID|1||{Rnd.Next(1000000)}||Test^Patient\r" +
                           $"OBR|1|{sample.SampleId}||\r" +
                           $"OBX|1|NM|{sample.TestName}||{sample.Result}|{sample.Unit}||||F\r";
                case ProtocolType.ASTM:
                    return $"H|\\^&|||AnalyzerEmulator\r" +
                           $"P|1||\r" +
                           $"O|1|{sample.SampleId}|\r" +
                           $"R|1|{sample.TestName}|{sample.Result}|{sample.Unit}\r" +
                           $"L|1|N\r";
                case ProtocolType.JSON:
                    return JsonSerializer.Serialize(sample);
                default:
                    return string.Empty;
            }
        }

        private static bool SendData(string data)
        {
            switch (Settings.Connection)
            {
                case ConnectionType.Network:
                    return Settings.Protocol switch
                    {
                        ProtocolType.ASTM => SendASTMOverNetwork(data),
                        ProtocolType.HL7 => SendHL7OverNetwork(data),
                        _ => SendRawOverNetwork(data)
                    };

                case ConnectionType.COM:
                    using (var port = new SerialPort(Settings.Target, 9600, Parity.None, 8, StopBits.One))
                    {
                        port.Open();
                        var bytes = Settings.Protocol == ProtocolType.ASTM
                            ? BuildAstmFrame(data)
                            : Encoding.UTF8.GetBytes(data);
                        port.Write(bytes, 0, bytes.Length);
                    }
                    return true;

                case ConnectionType.File:
                    var content = Settings.Protocol == ProtocolType.ASTM
                        ? Encoding.ASCII.GetString(BuildAstmFrame(data))
                        : data + Environment.NewLine;
                    File.AppendAllText(Settings.Target, content);
                    return true;

                default:
                    return false;
            }
        }

        private static bool SendHL7OverNetwork(string hl7Message)
        {
            var parts = Settings.Target.Split(':');
            using var client = new TcpClient();
            client.SendTimeout = Settings.TimeoutMs;
            client.ReceiveTimeout = Settings.TimeoutMs;

            try { client.Connect(parts[0], int.Parse(parts[1])); }
            catch (Exception ex)
            {
                Console.WriteLine($"[HOST UNREACHABLE] {ex.Message}");
                return false;
            }

            using var stream = client.GetStream();

            var msgBytes = Encoding.UTF8.GetBytes(hl7Message);
            using var ms = new MemoryStream();
            ms.WriteByte(VT);
            ms.Write(msgBytes, 0, msgBytes.Length);
            ms.WriteByte(FS);
            ms.WriteByte(CR);
            var frame = ms.ToArray();

            stream.Write(frame, 0, frame.Length);

            try
            {
                var ackBuf = new byte[1024];
                var read = stream.Read(ackBuf, 0, ackBuf.Length);
                ackBuf = ackBuf.Where(x => x > 0).ToArray();
                if (read > 3 && ackBuf[0] == VT && ackBuf.SkipLast(1).Last() == FS)
                {
                    var ack = Encoding.UTF8.GetString(ackBuf);
                    var isAck = ack.Contains("MSA|AA");
                    Console.WriteLine(isAck ? "[HL7 ACK]" : $"[HL7 NAK] {ack[..Math.Min(80, ack.Length)]}");
                    return isAck;
                }
            }
            catch (IOException)
            {
                Console.WriteLine("[TIMEOUT] HL7 ACK не получен");
            }

            return false;
        }

        private static bool SendRawOverNetwork(string data)
        {
            var parts = Settings.Target.Split(':');
            using var client = new TcpClient();
            client.SendTimeout = Settings.TimeoutMs;
            client.ReceiveTimeout = Settings.TimeoutMs;
            client.Connect(parts[0], int.Parse(parts[1]));

            using var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(data);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }

        private static bool SendASTMOverNetwork(string frameData)
        {
            var parts = Settings.Target.Split(':');
            using var client = new TcpClient();
            client.SendTimeout = Settings.TimeoutMs;
            client.ReceiveTimeout = Settings.TimeoutMs;

            try
            {
                client.Connect(parts[0], int.Parse(parts[1]));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HOST UNREACHABLE] {ex.Message}");
                return false;
            }

            using var stream = client.GetStream();

            // 1. Handshake: ENQ -> ждем ACK
            stream.WriteByte(ENQ);
            if (!WaitForAck(stream))
            {
                stream.WriteByte(EOT); // сигнализируем об отбое
                Console.WriteLine("[TIMEOUT] ACK не получен после ENQ. Отправка EOT.");
                return false;
            }

            // 2. Отправляем тело ASTM с STX/ETX и checksum
            var frame = BuildAstmFrame(frameData);
            stream.Write(frame, 0, frame.Length);

            // 3. Ждем ACK на тело
            if (!WaitForAck(stream))
            {
                stream.WriteByte(EOT);
                Console.WriteLine("[TIMEOUT] ACK не получен после передачи данных.");
                return false;
            }

            // 4. Завершаем передачу EOT
            stream.WriteByte(EOT);
            return true;
        }

        private static bool WaitForAck(NetworkStream stream)
        {
            try
            {
                var b = stream.ReadByte();
                return b == ACK;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static byte[] BuildAstmFrame(string frameData)
        {
            var dataBytes = Encoding.ASCII.GetBytes(frameData);

            var sum = STX + ETX;
            foreach (var b in dataBytes) sum += b;
            var cs = (byte)(sum & 0xFF);
            var csHex = cs.ToString("X2");

            using var ms = new MemoryStream();
            ms.WriteByte(STX);
            //ms.WriteByte((byte)'1');
            ms.Write(dataBytes, 0, dataBytes.Length);
            ms.WriteByte(ETX);
            ms.Write(Encoding.ASCII.GetBytes(csHex), 0, 2);
            ms.WriteByte(CR);
            ms.WriteByte(LF);
            return ms.ToArray();
        }
    }
}