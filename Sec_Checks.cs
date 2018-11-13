﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Security.Principal;

namespace SWELF
{
    class Sec_Checks
    {
        public static int UpTime
        {
            get
            {
                using (var uptime = new PerformanceCounter("System", "System Up Time"))
                {
                    uptime.NextValue();
                    return TimeSpan.FromSeconds(uptime.NextValue()).Days;
                }
            }
        }

        private static long Eventlog_Count_Before_Write = 0;


        public static bool Pre_Run_Sec_Checks()
        {
            if (Check_EventLog_Service() && Check_Reg_Keys())//Event logs requirements in place
            {
                return true;
            }
            else
            {
                FAILED_Sec_Check("Check_EventLog_Service() && Check_Reg_Keys()", "Windows Event Log Regkey or Service Missing or off. SWELF did not run due to possible tampering.", Errors.LogSeverity.Critical);
                return false;
            }
        }

        public static void Pre_Live_Run_Sec_Checks()
        {
            //TODO Fix non working algo for live run checks
            //Maybe reg values over time analysis?????
        }

        public static bool Live_Run_Sec_Checks(string EVT_Log_Name)
        {
            try
            {
                if (Settings.CHECK_If_EventLog_Exsits(EVT_Log_Name))
                {
                    if (Check_Event_Log_Is_Blank(EVT_Log_Name) && Check_Event_Log_Is_Blank(Settings.SWELF_EventLog_Name) && Check_Windows_Event_Log_Size(EVT_Log_Name) && Check_Windows_Event_Log_Retention_Policy(EVT_Log_Name) && Check_Event_Log_Has_Not_Recorded_Logs_In_X_Days(EVT_Log_Name))
                    {
                        GET_EventLog_Count_Before_Write(EVT_Log_Name);
                        return true;
                    }
                    else
                    {
                        GET_EventLog_Count_Before_Write(EVT_Log_Name);
                        return false;//FAILED
                    }
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                return false;//FAILED
            }
        }

        public static bool Post_Run_Sec_Checks()
        {
            if (Check_If_SWELF_Event_Logs_Written(Convert.ToInt32(Eventlog_Count_Before_Write), Settings.SWELF_Events_Of_Interest_Matching_EventLogs.Count))
            {
                return true;
            }
            else
            {
                return false;
            }
        }



        public static bool CHECK_If_EventLog_Missing(EventLog_File ELF, EventLog_Entry EVE)
        {
            if ((EVE.EventLog_Seq_num != ELF.ID_EVENTLOG + 1) && ELF.EventlogMissing == false && (ELF.ID_EVENTLOG != 0 && EVE.EventRecordID != 0))
            {
                ELF.EventlogMissing = true;
                Errors.WRITE_Errors_To_Log("CHECK_If_EventLog_Missing(EventLogFile ELF, EventLogEntry EVE)", "Logs on " + Settings.ComputerName + " under Event Log name " + EVE.LogName + " near or around Event ID " + EVE.EventRecordID.ToString() + " found Eventlogs missing.", Errors.LogSeverity.Critical);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static void GET_EventLog_Count_Before_Write(string EVT_Log_Name)
        {
            Eventlog_Count_Before_Write = EventLogSession.GlobalSession.GetLogInformation(Settings.SWELF_EventLog_Name, PathType.LogName).RecordCount.Value;
        }


        public static bool CHECK_If_Running_as_Admin()
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return true;
            }
            else
            {
                Errors.Log_Error("ERROR: Settings.CHECK_If_Running_as_Admin()", Settings.ComputerName + " SWELF not running as admin and was unable to read eventlogs.", Errors.LogSeverity.Critical);
                Errors.SEND_Errors_To_Central_Location();
                return false;
            }
        }

        private static bool Check_Reg_Keys()
        {
            List<string> RegKeys = new List<string>
            {
                @"System\CurrentControlSet\Services\Eventlog"
            };
            for (int x = 0; x < RegKeys.Count; ++x)
            {
                try
                {
                    RegistryKey reg = Registry.LocalMachine.OpenSubKey(RegKeys.ElementAt(x));
                    if (reg != null)
                    {

                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Errors.WRITE_Errors_To_Log("Check_Reg_Keys()", "FAILED Security Check Registry " + e.Message.ToString(), Errors.LogSeverity.Critical);
                    return false;
                }
            }
            return true;
        }

        private static bool Check_EventLog_Service()
        {
            try
            {
                using (ServiceController sc = new ServiceController("EventLog"))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                        return true;
                    else
                        return false;
                }
            }
            catch
            {
                FAILED_Sec_Check("Check_EventLog_Service()", "Failed Check for running service.", Errors.LogSeverity.Critical);
                return false;
            }
        }

        private static bool Check_If_SWELF_Event_Logs_Written(int NumberOfRecordsWritten_Before, int NumberOfRecordsWritten_After)
        {
            if ((NumberOfRecordsWritten_Before + NumberOfRecordsWritten_After) >= NumberOfRecordsWritten_Before)
            {
                return true;
            }
            else
            {
                FAILED_Sec_Check("Check_If_SWELF_Event_Logs_Written()", Settings.SWELF_EventLog_Name + " Eventlog is empty and logs did not write to event log.", Errors.LogSeverity.Critical);
                return false;//FAILED
            }
        }

        private static bool Check_Windows_Event_Log_Size(string EVT_Log_Name)
        {
            long Default_Min_EventLogSize = 64000;
            long EVT_Log_Namez_FileSize = EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).FileSize.Value;
            if (EVT_Log_Namez_FileSize < Default_Min_EventLogSize)
            {
                Errors.Log_Error("Check_Windows_Event_Log_Size()", "The " + EVT_Log_Name + " eventlog is smaller that the system log. This could be unintended modification", Errors.LogSeverity.Warning);
                return false;
            }
            return true;
        }

        private static bool Check_Windows_Event_Log_Retention_Policy(string EVT_Log_Name)
        {
            List<EventLog> eventLogs = EventLog.GetEventLogs().ToList();

            for (int x = 0; x < eventLogs.Count; ++x)
            {
                if (eventLogs.Any(s => eventLogs.ElementAt(x).Log.ToLower().IndexOf(s.Log.ToLower(), StringComparison.OrdinalIgnoreCase) > 0))
                {
                    RegistryKey regEventLog = Registry.LocalMachine.OpenSubKey("System\\CurrentControlSet\\Services\\EventLog\\" + eventLogs.ElementAt(x).Log);
                    if (regEventLog != null)
                    {
                        Object RegKeyFileAttrib = regEventLog.GetValue("File");
                        if (RegKeyFileAttrib != null)
                        {
                            switch (eventLogs.ElementAt(x).OverflowAction)
                            {
                                case OverflowAction.OverwriteOlder:
                                    Errors.Log_Error("Check_Windows_Event_Log_Retention_Policy()", eventLogs.ElementAt(x).LogDisplayName + " is set to not overwire only logs older than " + eventLogs.ElementAt(x).MinimumRetentionDays.ToString(), Errors.LogSeverity.Informataion);
                                    return true;
                                case OverflowAction.DoNotOverwrite:
                                    Errors.Log_Error("Check_Windows_Event_Log_Retention_Policy()", eventLogs.ElementAt(x).LogDisplayName + " is set to not overwire the oldest event log", Errors.LogSeverity.Informataion);
                                    return true;
                            }
                        }
                        else
                        {
                            Errors.Log_Error("Check_Windows_Event_Log_Retention_Policy()", eventLogs.ElementAt(x).LogDisplayName + RegKeyFileAttrib.ToString() + " \"File\" reg attrib does not exist and it should", Errors.LogSeverity.Critical);
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static bool Check_Event_Log_Has_Not_Recorded_Logs_In_X_Days(string EVT_Log_Name)
        {
            TimeSpan diff;

            DateTime Today = DateTime.Now;

            DateTime CreationTime = EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).CreationTime.Value;

            DateTime LastWriteTime = EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).LastWriteTime.Value;
            //For a given eventlog when was the last time it was written to. Based on today - last time X. Alert yes or no.

            diff = Today.Subtract(LastWriteTime);

            if (diff.Days < 0 && UpTime < 1)
            {
                FAILED_Sec_Check("Check_Windows_Event_Log_Has_Not_Recorded_Logs_In_X_Days()", "The Event Log " + EVT_Log_Name + " has not been written to in 24 hours.", Errors.LogSeverity.Warning);
                return false;//FAILED
            }

            diff = Today.Subtract(CreationTime);
            if (diff.Days <= 0)
            {
                FAILED_Sec_Check("Check_Windows_Event_Log_Has_Not_Recorded_Logs_In_X_Days()", "The Event Log " + EVT_Log_Name + " was created in the last 24 hours.", Errors.LogSeverity.Critical);
                return false;//FAILED
            }

            if (EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).IsLogFull.Value && EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).RecordCount < 10)
            {
                FAILED_Sec_Check("Check_Windows_Event_Log_Has_Not_Recorded_Logs_In_X_Days()", "The Event Log " + EVT_Log_Name + " is full amd has less than 10 records.", Errors.LogSeverity.Critical);
                return false;//FAILED
            }
            return true;
        }

        private static bool Check_Event_Log_Is_Blank(string EVT_Log_Name)
        {
            try
            {
                if (Settings.CHECK_If_EventLog_Exsits(EVT_Log_Name))
                {
                    if (EventLogSession.GlobalSession.GetLogInformation(EVT_Log_Name, PathType.LogName).RecordCount.Value > 1)
                    {
                        return true;
                    }
                    else
                    {
                        if (EventLogSession.GlobalSession.GetLogInformation(Settings.SWELF_EventLog_Name, PathType.LogName).RecordCount.Value > 0)
                        {
                            return true;
                        }
                        else if (EVT_Log_Name == Settings.SWELF_EventLog_Name)
                        {
                            FAILED_Sec_Check("Check_Event_Log_Is_Blank()", EVT_Log_Name + " Eventlog is empty.", Errors.LogSeverity.Critical);
                            return false;//FAILED
                        }
                        else
                        {
                            FAILED_Sec_Check("Check_Event_Log_Is_Blank()", EVT_Log_Name + " Eventlog is empty.", Errors.LogSeverity.Critical);
                            return false;//FAILED
                        }
                    }
                }
                else
                {
                    return false;//FAILED
                }
            }
            catch (Exception e)
            {
                FAILED_Sec_Check("Check_Event_Log_Is_Blank()", EVT_Log_Name + " "+e.Message.ToString(), Errors.LogSeverity.Warning);
                return false;//FAILED
            }
        }

        private static void FAILED_Sec_Check(string Method, string DataOnFail, Errors.LogSeverity LogSeverity)
        {
            Errors.Log_Error(Method, DataOnFail, LogSeverity);
            EventLog_SWELF.WRITE_Critical_EventLog(Method + " : " + DataOnFail);
        }

    }
}