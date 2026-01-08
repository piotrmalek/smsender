/******************************************************************************
smsender.cs
Command-line SMS Sender Utility using SMSAPI (smsapi.pl)

Author: Piotr Małek
Repository: https://github.com/piotrmalek/smsender

SmsSender is a lightweight, production-ready command-line application for
sending SMS messages via the SMSAPI service (https://www.smsapi.pl).

The tool is designed to be used as a standalone executable in scripts,
automation workflows, monitoring systems, and industrial applications.
It is actively used in SCADA environments to deliver alarm and fault
notifications, but it is not limited to SCADA and can be used in any context
where reliable SMS delivery from the command line is required.

Key features:
- Robust command-line argument parsing
- Support for multiple recipients (-p can be used multiple times)
- Free-form message text (-m) with full support for '-' characters
- Secure storage of API tokens using Windows DPAPI
  (CurrentUser or LocalMachine scope)
- Clear, color-coded console output for errors, warnings, and status messages
- Consistent exit codes suitable for scripting and automation
- Masking of sensitive data (API token) in console output

Supported command-line options:
- -t   <token>   : save API token for current user (DPAPI CurrentUser)
- -tm  <token>   : save API token for local machine (DPAPI LocalMachine)
- -s   <sender>  : sender name
- -p   <phone>   : recipient phone number (can be used multiple times)
- -m   <message> : SMS message text
- -h, -help      : display usage information

Exit codes:
- 0 : success (SMS sent or token saved)
- 1 : runtime or API error
- 2 : invalid or missing command-line parameters

Typical use cases:
- Alarm and fault notifications in SCADA and industrial control systems
- Server, service, and infrastructure monitoring
- Automation scripts and scheduled tasks
- Manual SMS sending from the command line

Security notes:
- API tokens are encrypted using Windows DPAPI and are bound to the selected
  security scope (CurrentUser or LocalMachine).
- Encrypted token files cannot be decrypted on another machine or under a
  different user account.
- API tokens are never printed in full; console output shows masked values only.

Notes:
- Phone number validation is intentionally minimal; final validation is
  performed by the SMSAPI service.
- Message parsing stops only on recognized flags, allowing messages such as
  "-10% voltage drop" without issues.
- Console colors are automatically disabled when output is redirected.

License:
This software is distributed under the MIT License.

Distributed as-is; no warranty is given.
******************************************************************************/



using System.Security.Cryptography;
using System.Text;
using SMSApi.Api;

namespace smsender
{
    internal class Sender
    {
        
        static String token_path = AppDomain.CurrentDomain.BaseDirectory + "token.dat";

        static string help_msg =
@"Usage:
    smsender -s <sender> -p <phone> [-p <phone2> ...] -m <message...>
    smsender -t  <token>   (save token for current user)
    smsender -tm <token>   (save token for local machine)
Examples:
    smsender -t XXX
    smsender -s Sender -p 48111111111 -p 48222222222 -m Warning! Device down!";

        static void SaveToken(string path, string token, bool scope_machine = false)
        {
            DataProtectionScope scope = DataProtectionScope.CurrentUser;
            if (scope_machine) scope = DataProtectionScope.LocalMachine; 
            byte[] data = Encoding.UTF8.GetBytes(token);
            byte[] protectedData = ProtectedData.Protect(
                data,
                optionalEntropy: null, // here you can add your own "salt"
                scope: scope 
            );

            File.WriteAllBytes(path, protectedData);
            PrintMessage("New Token saved.", MessageType.SUCCESS);
        }

        static string LoadToken(string path)
        {
            byte[] protectedData = File.ReadAllBytes(path);

            try
            {
                var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (CryptographicException)
            {
                var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(data);
            }
        }

        enum MessageType
        {
            ERROR,
            WARNING,
            SUCCESS,
            INFO
        }

        static void PrintMessage(string msg, MessageType type)
        {
            var old = Console.ForegroundColor;

            ConsoleColor color = type switch
            {
                MessageType.ERROR => ConsoleColor.Red,
                MessageType.WARNING => ConsoleColor.Yellow,
                MessageType.SUCCESS => ConsoleColor.Green,
                MessageType.INFO => ConsoleColor.Cyan,
                _ => old
            };

            string prefix = type switch
            {
                MessageType.ERROR => "[ERROR] ",
                MessageType.WARNING => "[WARN ] ",
                MessageType.SUCCESS => "[OK   ] ",
                MessageType.INFO => "[INFO ] ",
                _ => ""
            };

            if (!Console.IsOutputRedirected)
                Console.ForegroundColor = color;

            Console.Write(prefix);
            Console.ForegroundColor = old;
            Console.WriteLine(msg);
        }

        static void PrintHelp()
        {
            Console.WriteLine();
            Console.WriteLine(help_msg);
        }

        private static int Main(string[] args)
        {
            string SMStoken = "";
            List<string> SMSphones = new List<string>();
            string SMSmessage = "";
            string SMSsender = "";
            bool tokenFromArgs = false;

            try
            {
                var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "-t", "-tm", "-s", "-p", "-m", "-h", "-help"
                };
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("No value after -t");
                        SMStoken = args[++i];
                        if (string.IsNullOrWhiteSpace(SMStoken)) throw new ArgumentException("Token cannot be empty");
                        if (SMStoken.Length < 10) throw new ArgumentException("Token looks too short");
                        SaveToken(token_path, SMStoken);
                        tokenFromArgs = true;
                        continue;                        
                    }
                    if (arg.Equals("-tm", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("No value after -tm");
                        SMStoken = args[++i];
                        if (string.IsNullOrWhiteSpace(SMStoken)) throw new ArgumentException("Token cannot be empty");
                        if (SMStoken.Length < 10) throw new ArgumentException("Token looks too short");
                        SaveToken(token_path, SMStoken, true);
                        tokenFromArgs = true;
                        continue;
                    }
                    if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("No value after -s");
                        SMSsender = args[++i];
                        if (SMSsender.StartsWith("-")) throw new ArgumentException("No value after -s");
                        continue;
                    }
                    if (arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("No value after -p");
                        var phone = args[++i];
                        if (phone.StartsWith("-")) throw new ArgumentException("No value after -p");
                        SMSphones.Add(phone);
                        continue;
                    }
                    if (arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                    {
                        // collect words until the next known flag or until the end
                        var parts = new List<string>();

                        while (i + 1 < args.Length && !flags.Contains(args[i + 1]))
                        {
                            parts.Add(args[++i]); 
                        }

                        // allow multiple -m (append)
                        if (parts.Count > 0)
                        {
                            if (!string.IsNullOrWhiteSpace(SMSmessage)) SMSmessage += " ";
                            SMSmessage += string.Join(" ", parts);
                        }

                        continue;
                    }
                    if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("-help", StringComparison.OrdinalIgnoreCase))
                    {
                        PrintHelp();
                        return 0;
                    }
                }
            }
            catch (System.Exception e)
            {
                PrintMessage(e.Message, MessageType.ERROR);
                return 1;
            }

            if (!tokenFromArgs)
            {
                try
                {
                    SMStoken = LoadToken(token_path);
                }
                catch (FileNotFoundException) { }
                catch (CryptographicException)
                {
                    PrintMessage("Saved token cannot be decrypted. Use -t or -tm.", MessageType.ERROR);
                    PrintHelp();
                    return 1;
                }
            }

            try
            {
                if (tokenFromArgs && string.IsNullOrWhiteSpace(SMSsender) && SMSphones.Count == 0 && string.IsNullOrWhiteSpace(SMSmessage))
                {
                    PrintMessage("Token saved. No SMS sent.", MessageType.INFO);
                    return 0;
                }

                bool allargspresent = true;
                if (string.IsNullOrWhiteSpace(SMStoken))
                {
                    allargspresent = false;
                    PrintMessage("Token invalid", MessageType.ERROR);
                }
                if (string.IsNullOrWhiteSpace(SMSsender))
                {
                    allargspresent = false;
                    PrintMessage("Sender invalid", MessageType.ERROR);
                }
                if (SMSphones.Count == 0)
                {
                    allargspresent = false;
                    PrintMessage("Phone invalid", MessageType.ERROR);
                }
                if (string.IsNullOrWhiteSpace(SMSmessage))
                {
                    allargspresent = false;
                    PrintMessage("Message invalid", MessageType.ERROR);
                }

                if (!allargspresent)
                {
                    PrintMessage("SMS not sent.", MessageType.WARNING);
                    PrintHelp();
                    return 2;
                }
                else
                {
                    string temptoken = SMStoken.Length <= 3 ? SMStoken : SMStoken[..3] + new string('*', SMStoken.Length - 3);

                    string phones = string.Join(",", SMSphones);
                    
                    SMSmessage = SMSmessage.Trim();

                    PrintMessage($"Sending SMS...", MessageType.INFO);
                    PrintMessage($"Token: {temptoken}", MessageType.INFO);
                    PrintMessage($"Sender name: {SMSsender}", MessageType.INFO);
                    PrintMessage($"Phones: {phones}", MessageType.INFO);
                    PrintMessage($"Message: {SMSmessage}", MessageType.INFO);

                    IClient client = new ClientOAuth(SMStoken);

                    var smsApi = new SMSFactory(client, new ProxyHTTP("https://api.smsapi.pl/"));

                    var result =
                        smsApi.ActionSend()
                            .SetText(SMSmessage)
                            .SetTo(phones)
                            .SetSender(SMSsender)
                            .Execute();

                    PrintMessage("Message sent.", MessageType.SUCCESS);
                } 
            }
            catch (ActionException e)
            {
                PrintMessage(e.Message, MessageType.ERROR);
                return 1;
            }
            catch (ClientException e)
            {
                PrintMessage(e.Message, MessageType.ERROR);
                return 1;
            }
            catch (HostException e)
            {
                PrintMessage(e.Message, MessageType.ERROR);
                return 1;
            }
            catch (ProxyException e)
            {
                PrintMessage(e.Message, MessageType.ERROR);
                return 1;
            }
            return 0;
        }
    }
}