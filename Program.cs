using LibreHardwareMonitor.Hardware;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PicoServer
{
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }


    class Program
    {

        enum CONNECTION_STATE
        {
            LISTENING,
            CONNECTED
        }
        public static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] bytes = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, bytes, 0, first.Length);
            Buffer.BlockCopy(second, 0, bytes, first.Length, second.Length);
            return bytes;
        }
        static float GetCPUTemp(Computer computer)
        {
            foreach (IHardware hardware in computer.Hardware)
            {
                ISensor sensor = Array.Find(hardware.Sensors, s => s.Name.Equals("Core (Tctl/Tdie)"));
                if (sensor != null)
                {
                    hardware.Update();
                    return (float)sensor.Value;
                }
            }

            return 0.0f;
        }

        static float GetGPUTemp(Computer computer)
        {
            foreach (IHardware hardware in computer.Hardware)
            {
                ISensor sensor = Array.Find(hardware.Sensors, s => s.Name.Equals("GPU Core"));
                if (sensor != null)
                {
                    hardware.Update();
                    return (float)sensor.Value;
                }
            }

            return 0.0f;
        }

        static void Main(string[] args)
        {
            Computer computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };

            computer.Open();
            computer.Accept(new UpdateVisitor());

            Console.WriteLine("Pico server v0.1");

            Random rnd = new Random();

            IPAddress ipAddress = Array.Find(Dns.GetHostEntry("localhost").AddressList,  a => a.AddressFamily == AddressFamily.InterNetwork);
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 2727);

            Socket listener = new Socket(ipAddress.AddressFamily,SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, 2727));
            listener.Listen(1);

            Socket handler = null;


            Console.WriteLine("Waiting for data....");

            CONNECTION_STATE currentState = CONNECTION_STATE.LISTENING;
            while (true)
            {
                switch(currentState)
                {
                    case CONNECTION_STATE.LISTENING:
                        Console.WriteLine("Waiting for a connection...");
                        try
                        {
                            handler = listener.Accept();
                            handler.ReceiveTimeout = 1000;
                            currentState = CONNECTION_STATE.CONNECTED;
                            Console.WriteLine("Connection accepted from {0}!", handler.RemoteEndPoint.ToString());
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.ToString());
                            break;
                        }
                        break;
                    case CONNECTION_STATE.CONNECTED:
                        byte[] bytes = new byte[1024];
                        int bytesRec = 0;
                        try
                        {
                            bytesRec = handler.Receive(bytes);
                        }
                        catch(SocketException e)
                        {
                            Console.WriteLine("Socket Disconnected");
                            currentState = CONNECTION_STATE.LISTENING;
                            continue;
                        }

                        if (bytesRec == 0)
                            continue;

                        string receivedString = Encoding.ASCII.GetString(bytes, 0, bytesRec);

                        if (receivedString.Equals("stats_req"))
                        {
                            float cpuTemp = GetCPUTemp(computer);
                            float gpuTemp = GetGPUTemp(computer);
                            byte[] cpuTempBytes = BitConverter.GetBytes(cpuTemp);
                            byte[] gpuTempBytes = BitConverter.GetBytes(gpuTemp);

                            byte[] reply = Combine(cpuTempBytes, gpuTempBytes);

                            handler.Send(reply);
                            Console.WriteLine("Got stats request, sending cpu: {0}, gpu: {1}", cpuTemp, gpuTemp);
                        }
                        else if (receivedString.Equals("disconnect_req"))
                        {
                            break;
                        }
                        break;
                }

                
            }

            handler.Shutdown(SocketShutdown.Both);
            handler.Close();

            computer.Close();
        }
    }
}
