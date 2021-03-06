﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using Newtonsoft.Json;
using System.Threading;

namespace NiceHashMiner
{
    public class EtherProxy_Proxy
    {
        public string listen;
        public string clientTimeout;
        public string blockRefreshInterval;
        public string hashrateWindow;
        public bool submitHashrate;
        public string luckWindow;
        public string largeLuckWindow;
    }

    public class EtherProxy_Frontend
    {
        public string listen;
        public string login;
        public string password;
    }

    public class EtherProxy_Upstream
    {
        public bool pool;
        public string name;
        public string url;
        public string timeout;
    }

    public class EtherProxy
    {
        public int threads;
        public EtherProxy_Proxy proxy;
        public EtherProxy_Frontend frontend;
        public string upstreamCheckInterval;
        public EtherProxy_Upstream[] upstream;
        public bool newrelicEnabled;
        public string newrelicName;
        public string newrelicKey;
        public bool newrelicVerbose;

        public EtherProxy()
        {
            // Set defaults
            threads = 2;

            proxy = new EtherProxy_Proxy();
            proxy.listen = "0.0.0.0:" + Config.ConfigData.APIBindPortEthereumProxy;
            proxy.clientTimeout = "5m";
            proxy.blockRefreshInterval = "100ms";
            proxy.hashrateWindow = "15m";
            proxy.submitHashrate = false;
            proxy.luckWindow = "24h";
            proxy.largeLuckWindow = "72h";

            frontend = new EtherProxy_Frontend();
            frontend.listen = "0.0.0.0:" + Config.ConfigData.APIBindPortEthereumFrontEnd;
            frontend.login = "admin";
            frontend.password = "";
            upstreamCheckInterval = "5s";

            upstream = new EtherProxy_Upstream[1];
            upstream[0] = new EtherProxy_Upstream();
            upstream[0].pool = true;
            upstream[0].name = "NiceHash";
            upstream[0].url = "";
            upstream[0].timeout = "10s";

            newrelicEnabled = false;
            newrelicName = "MyEtherProxy";
            newrelicKey = "SECRET_KEY";
            newrelicVerbose = false;
        }
    }

    public class EthMiner
    {
        public string name;
        public long hashrate;
        public bool timeout;
        public bool warning;
    }

    public class Eth<T>
    {
        public EthMiner[] miners;
    }

    public static class Ethereum
    {
        public static Process ProcessProxyHandle;
        public static string EtherMinerPath;
        public static string EtherProxyPath;
        public static string EtherProxyConfigPath;
        public static int EtherMinerRunning;
        private static string CurrentBlockNum;
        private static readonly object _locker;

        static Ethereum()
        {
            EtherMinerPath = "bin\\ethereum\\ethminer.exe";
            EtherProxyPath = "ether-proxy.exe";
            EtherProxyConfigPath = "config.json";
            EtherMinerRunning = 0;
            CurrentBlockNum = "";
            _locker = new object();
        }

        public static bool StartProxy(bool writeconfig, string url, string username)
        {
            lock (_locker)
            {
                if (EtherMinerRunning == 0)
                {
                    if (writeconfig)
                    {
                        // Prepare and write config file
                        EtherProxy ep = new EtherProxy();
                        if (File.Exists(EtherProxyConfigPath)) ep = JsonConvert.DeserializeObject<EtherProxy>(File.ReadAllText(EtherProxyConfigPath));
                        ep.upstream[0].url = "http" + url.Substring(11) + "/n1c3-" + username + "/" + TotalSpeed();
                        try { File.WriteAllText("bin\\ethereum\\" + EtherProxyConfigPath, JsonConvert.SerializeObject(ep, Formatting.Indented)); }
                        catch (Exception e) { Helpers.ConsolePrint("Ethereum", "WriteConfigFile: " + e.ToString()); return false; }
                    }

                    // Setup and start proxy
                    Helpers.ConsolePrint("Ethereum", "StartProxy: " + "Starting proxy..");
                    Process P = new Process();
                    P.StartInfo.FileName = EtherProxyPath;
                    P.StartInfo.Arguments = " " + EtherProxyConfigPath;
                    P.StartInfo.WorkingDirectory = "bin\\ethereum";
                    P.StartInfo.CreateNoWindow = Config.ConfigData.HideMiningWindows;
                    P.StartInfo.UseShellExecute = !Config.ConfigData.HideMiningWindows;
                    P.EnableRaisingEvents = true;
                    P.Exited += P_Exited;

                    if (Config.ConfigData.HideMiningWindows)
                    {
                        P.StartInfo.FileName = "cmd";
                        P.StartInfo.Arguments = " /C \" " + EtherProxyPath + " " + EtherProxyConfigPath + "\"";
                    }

                    try { P.Start(); }
                    catch
                    {
                        Helpers.ConsolePrint("Ethereum", "StartProxy: Failed to start proxy..");
                        return false;
                    }
                    ProcessProxyHandle = P;

                    EtherMinerRunning = 1;
                }
                else
                    EtherMinerRunning++;

                Helpers.ConsolePrint("Ethereum", "EtherMinerRunning: " + EtherMinerRunning);
            }

            return true;
        }

        static void P_Exited(object sender, EventArgs e)
        {
            lock (_locker)
            {
                if (ProcessProxyHandle != null)
                {
                    try { ProcessProxyHandle.Kill(); }
                    catch { }

                    ProcessProxyHandle.Close();
                    ProcessProxyHandle = null;
                    EtherMinerRunning = 0;
                }
            }
        }

        public static void StopProxy()
        {
            lock (_locker)
            {
                if (ProcessProxyHandle != null)
                {
                    if (EtherMinerRunning > 1)
                    {
                        EtherMinerRunning--;
                        return;
                    }
                    else
                    {
                        Helpers.ConsolePrint("Ethereum", "StopProxy: " + "Exiting proxy..");
                        try { ProcessProxyHandle.Kill(); }
                        catch { Helpers.ConsolePrint("Ethereum", "StopProxy: " + "Ethereum proxy failed to exit.."); }

                        foreach (Process process in Process.GetProcessesByName("ether-proxy"))
                        {
                            try { process.Kill(); }
                            catch { Helpers.ConsolePrint("Ethereum", "StopProxy: " + "Ethereum proxy failed to exit.."); }
                        }

                        ProcessProxyHandle.Close();
                        ProcessProxyHandle = null;
                        EtherMinerRunning = 0;
                    }
                }
            }
        }

        public static bool CreateDAGFile(string worker)
        {
            try
            {
                if (!GetCurrentBlock(worker)) throw new Exception("GetCurrentBlock returns null..");

                Helpers.ConsolePrint("Ethereum", "Creating DAG file for " + worker + "..");
                Process P = new Process();
                P.StartInfo.FileName = EtherMinerPath;
                P.StartInfo.Arguments = " --dag-dir " + Config.ConfigData.DAGDirectory + " --create-dag " + CurrentBlockNum;
                P.StartInfo.CreateNoWindow = Config.ConfigData.HideMiningWindows;
                P.StartInfo.UseShellExecute = !Config.ConfigData.HideMiningWindows;
                P.Start();
                P.WaitForExit();

                P.Close();
                P = null;

                foreach (var file in Directory.GetFiles(Config.ConfigData.DAGDirectory))
                {
                    string dest = Path.Combine(Config.ConfigData.DAGDirectory + "\\" + worker, Path.GetFileName(file));
                    if (file.Contains("full") && !File.Exists(dest)) File.Copy(file, dest, false);
                }
            }
            catch (Exception e)
            {
                Helpers.ConsolePrint("Ethereum", "Exception: " + e.ToString());
                return false;
            }

            return true;
        }

        private static bool KillAllRunningProxy()
        {
            Process[] check = Process.GetProcessesByName("ether-proxy");

            if (check == null) return false;

            foreach (Process process in check)
            {
                try { process.Kill(); }
                catch (Exception e) { Helpers.ConsolePrint("Ethereum", "KillAllRunningProxy: " + e.ToString()); }
            }

            return true;
        }

        private static int TotalSpeed()
        {
            double totalspeed = 0.0;

            for (int i = 0; i < Config.ConfigData.Groups.Length; i++)
            {
                if (Config.ConfigData.Groups[i].Name.Equals("NVIDIA5.x") ||
                    Config.ConfigData.Groups[i].Name.Equals("AMD_OpenCL"))
                {
                    for (int j = 0; j < Config.ConfigData.Groups[i].Algorithms.Length; j++)
                        if (Config.ConfigData.Groups[i].Algorithms[j].Name.Equals("ethereum"))
                            totalspeed += Config.ConfigData.Groups[i].Algorithms[j].BenchmarkSpeed;
                }
            }

            return Convert.ToInt32(totalspeed / 1000000);
        }

        private static bool GetCurrentBlock(string worker)
        {
            string ret = NiceHashStats.GetNiceHashAPIData("https://etherchain.org/api/blocks/count", worker);
            if (ret == null) return false;
            ret = ret.Substring(ret.LastIndexOf("count") + 7);
            CurrentBlockNum = ret.Substring(0, ret.Length - 3);

            return true;
        }
    }
}
