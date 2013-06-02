﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Linq;

using UnityEngine;
using KSP;

namespace KerbalAlarmClock
{
    public partial class KACWorker
    {
        KACAlarm.AlarmType AddType = KACAlarm.AlarmType.Raw;
        KACAlarm.AlarmAction AddAction = KACAlarm.AlarmAction.MessageOnly;

        KerbalTimeStringArray timeRaw = new KerbalTimeStringArray(600);
        KerbalTimeStringArray timeMargin = new KerbalTimeStringArray();

        private String strAlarmName = "";
        private String strAlarmNotes = "";
        //private String strAlarmNotes = "";
        //private String strAlarmDetail = "";
        private Boolean blnAlarmAttachToVessel=true;

        private String strAlarmDescSOI = "This will monitor the current active flight path for the next detected SOI change.\r\n\r\nIf the SOI Point changes the alarm will adjust until it is within {0} seconds of the Alarm time, at which point it just maintains the last captured time of the change.";
        private String strAlarmDescXfer = "This will check and recalculate the active transfer alarms for the correct phase angle - the math for these is based around circular orbits so the for any elliptical orbit these need to be recalculated over time.\r\n\r\nThe alarm will adjust until it is within {0} seconds of the target phase angle, at which point it just maintains the last captured time of the angle.\r\nI DO NOT RECOMMEND TURNING THIS OFF UNLESS THERE IS A MASSIVE PERFORMANCE GAIN";
        private String strAlarmDescNode = "This will check and recalculate the active orbit node alarms as the flight path changes. The alarm will adjust until it is within {0} seconds of the node.";


        /// <summary>
        /// Code to reset the settings etc when athe new button is hit
        /// </summary>
        private void NewAddAlarm()
        {
            //Set time variables
            timeRaw.BuildFromUT(600);
            strRawUT = "";
            _ShowAddMessages = false;

            //option for xfer mode
            if (Settings.XferUseModelData)
                intXferType = 0;
            else
                intXferType = 1;

            //default margin
            timeMargin.BuildFromUT(Settings.AlarmDefaultMargin);

            //set default strings
            strAlarmName = FlightGlobals.ActiveVessel.vesselName + "";
            strAlarmNotes = "";

            AddAction = (KACAlarm.AlarmAction)Settings.AlarmDefaultAction;
            //blnHaltWarp = true;

            //set initial alarm type based on whats on the flight path
            if (KACWorkerGameState.ManeuverNodeExists)
                AddType = KACAlarm.AlarmType.Maneuver;//AddAlarmType.Node;
            else if (KACWorkerGameState.SOIPointExists)
                AddType = KACAlarm.AlarmType.SOIChange;//AddAlarmType.Node;
            else
                AddType = KACAlarm.AlarmType.Raw;//AddAlarmType.Node;

            //trigger the work to set each type
            AddTypeChanged();

            //build the XFer parents list
            SetUpXferParents();
            //if the craft is orbiting a body on the parents list then set it as the default
            if (XferParentBodies.Contains(KACWorkerGameState.CurrentVessel.mainBody.referenceBody))
            {
                intXferCurrentParent = XferParentBodies.IndexOf(KACWorkerGameState.CurrentVessel.mainBody.referenceBody);
                SetupXferOrigins();
                intXferCurrentOrigin = XferOriginBodies.IndexOf(KACWorkerGameState.CurrentVessel.mainBody);
            }
            else
            {
                intXferCurrentParent = 0;
                SetupXferOrigins();
                intXferCurrentOrigin = 0;
            }
            //set initial targets
            SetupXFerTargets();
            intXferCurrentTarget = 0;
        }

        String strAlarmEventName = "Alarm";
        public void AddTypeChanged()
        {
            if (AddType == KACAlarm.AlarmType.Transfer || AddType == KACAlarm.AlarmType.TransferModelled)
                blnAlarmAttachToVessel = false;
            else
                blnAlarmAttachToVessel = true;

            //set strings, etc here for type changes
            switch (AddType)
            {
                case KACAlarm.AlarmType.Raw:
                    strAlarmEventName = "Alarm";
                    BuildRawStrings();
                    break;
                case KACAlarm.AlarmType.Maneuver:
                    strAlarmEventName = "Node";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Maneuver Node";
                    break;
                case KACAlarm.AlarmType.SOIChange:
                    strAlarmEventName = "SOI";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing SOI Change\r\n" +
                                        "     Old SOI: " + KACWorkerGameState.CurrentVessel.orbit.referenceBody.bodyName + "\r\n" +
                                        "     New SOI: " + KACWorkerGameState.CurrentVessel.orbit.nextPatch.referenceBody.bodyName;
                    break;
                case KACAlarm.AlarmType.Transfer:
                case KACAlarm.AlarmType.TransferModelled:
                    strAlarmEventName = "Transfer";
                    BuildTransferStrings();
                    break;
                case KACAlarm.AlarmType.Apoapsis:
                    strAlarmEventName = "Apoapsis";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Apoapsis";
                    break;
                case KACAlarm.AlarmType.Periapsis:
                    strAlarmEventName = "Periapsis";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Periapsis";
                    break;
                case KACAlarm.AlarmType.AscendingNode:
                    strAlarmEventName = "Ascending";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Ascending Node";
                    break;
                case KACAlarm.AlarmType.DescendingNode:
                    strAlarmEventName = "Descending";
                    strAlarmName = KACWorkerGameState.CurrentVessel.vesselName;
                    strAlarmNotes = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Descending Node";
                    break;
                default:
                    strAlarmEventName = "Alarm"; 
                    break;
            }
        }

        private void BuildTransferStrings()
        {
            String strWorking = "";
            if (blnAlarmAttachToVessel)
                strWorking = "Time to pay attention to\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nNearing Celestial Transfer:";
            else
                strWorking = "Nearing Celestial Transfer:";

            if (XferTargetBodies !=null && intXferCurrentTarget<XferTargetBodies.Count)
                strWorking += "\r\n\tOrigin: " + XferTargetBodies[intXferCurrentTarget].Origin.bodyName + "\r\n\tTarget: " + XferTargetBodies[intXferCurrentTarget].Target.bodyName;
            strAlarmNotes= strWorking;

            strWorking = "";
            if (XferTargetBodies != null && intXferCurrentTarget < XferTargetBodies.Count)
                strWorking = XferTargetBodies[intXferCurrentTarget].Origin.bodyName + "->" + XferTargetBodies[intXferCurrentTarget].Target.bodyName;
            else
                strWorking = strWorking = "Nearing Celestial Transfer";
            strAlarmName = strWorking;
        }

        private void BuildRawStrings()
        {
            String strWorking = "";
            if (blnAlarmAttachToVessel)
                strWorking = "Time to pay attention to:\r\n    " + KACWorkerGameState.CurrentVessel.vesselName + "\r\nRaw Time Alarm";
            else
                strWorking = "Raw Time Alarm";
            strAlarmNotes = strWorking;

            strWorking = "";
            if (blnAlarmAttachToVessel)
                strWorking = KACWorkerGameState.CurrentVessel.vesselName;
            else
                strWorking = "Raw Time Alarm";
            strAlarmName = strWorking;

        }


        //String[] strAddTypes = new String[] { "Raw", "Maneuver","SOI","Transfer" };
        String[] strAddTypes = new String[] { "R", "M", "A", "P", "A", "D", "S", "X" };

        GUIContent[] guiTypes = new GUIContent[]
            {
                new GUIContent(KACResources.btnRaw,"Raw Time Alarm"),
                new GUIContent(KACResources.btnMNode,"Maneuver Node"),
                new GUIContent(KACResources.btnAp,"Apoapsis"),
                new GUIContent(KACResources.btnPe,"Periapsis"),
                new GUIContent(KACResources.btnAN,"Ascending Node"),
                new GUIContent(KACResources.btnDN,"Descending Node"),
                new GUIContent(KACResources.btnSOI,"SOI Change"),
                new GUIContent(KACResources.btnXfer,"Transfer Window")
            };

        KACAlarm.AlarmType[] TypesForAttachOption = new KACAlarm.AlarmType[] 
            { 
                KACAlarm.AlarmType.Raw, 
                KACAlarm.AlarmType.Transfer, 
                KACAlarm.AlarmType.TransferModelled 
            };

        int intHeight_AddWindowCommon;
        /// <summary>
        /// Draw the Add Window contents
        /// </summary>
        /// <param name="WindowID"></param>
        public void FillAddWindow(int WindowID)
        {
            GUILayout.BeginVertical();

            //AddType =  (KACAlarm.AlarmType)GUILayout.Toolbar((int)AddType, strAddTypes,KACResources.styleButton);
            GUIContent[] guiButtons = guiTypes;
            if (DrawButtonList(ref AddType,guiButtons))
            {
                AddTypeChanged();
            }

            //calc height for common stuff
            intHeight_AddWindowCommon = 64;
            if (AddType != KACAlarm.AlarmType.Raw) //add stuff for margins
                intHeight_AddWindowCommon += 28;
            if (TypesForAttachOption.Contains(AddType)) //add stuff for attach to ship
                intHeight_AddWindowCommon += 30;

            //layout the right fields for the common components
            Boolean blnAttachPre = blnAlarmAttachToVessel;
            WindowLayout_CommonFields2(ref strAlarmName, ref blnAlarmAttachToVessel, ref AddAction, ref timeMargin, AddType, intHeight_AddWindowCommon);

            //layout the specific pieces for each type of alarm
            switch (AddType)
            {
                case KACAlarm.AlarmType.Raw:
                    if (blnAttachPre!=blnAlarmAttachToVessel) BuildRawStrings();
                    WindowLayout_AddPane_Raw();
                    break;
                case KACAlarm.AlarmType.Maneuver:
                    WindowLayout_AddPane_Maneuver();
                    break;
                case KACAlarm.AlarmType.SOIChange:
                    WindowLayout_AddPane_NodeEvent(KACWorkerGameState.SOIPointExists, KACWorkerGameState.CurrentVessel.orbit.UTsoi - KACWorkerGameState.CurrentTime.UT);
                    //WindowLayout_AddPane_SOI2();
                    break;
                case KACAlarm.AlarmType.Transfer:
                case KACAlarm.AlarmType.TransferModelled:
                    if (blnAttachPre != blnAlarmAttachToVessel) BuildTransferStrings();
                    WindowLayout_AddPane_Transfer();
                    break;
                case KACAlarm.AlarmType.Apoapsis:
                    WindowLayout_AddPane_NodeEvent(KACWorkerGameState.ApPointExists, KACWorkerGameState.CurrentVessel.orbit.timeToAp);
                    break;
                case KACAlarm.AlarmType.Periapsis:
                    WindowLayout_AddPane_NodeEvent(KACWorkerGameState.PePointExists, KACWorkerGameState.CurrentVessel.orbit.timeToPe);
                    break;
                case KACAlarm.AlarmType.AscendingNode:
                    //work out the target orbit type and then call the functions    
                    Double timeToAN;
                    Boolean blnANExists = KACUtils.CalcTimeToANorDN(KACWorkerGameState.CurrentVessel, KACUtils.ANDNNodeType.Ascending, out timeToAN);
                    WindowLayout_AddPane_NodeEvent(blnANExists, timeToAN);
                    break;
                case KACAlarm.AlarmType.DescendingNode:
                    Double timeToDN;
                    Boolean blnDNExists = KACUtils.CalcTimeToANorDN(KACWorkerGameState.CurrentVessel, KACUtils.ANDNNodeType.Descending, out timeToDN);
                    WindowLayout_AddPane_NodeEvent(blnDNExists, timeToDN);
                    break;
                default:
                    break;
            }

            GUILayout.EndVertical();
            
            SetTooltipText();
        }


        ////Variabled for Raw Alarm screen
        //String strYears = "0", strDays = "0", strHours = "0", strMinutes = "0",
        String strRawUT="0";
        KerbalTime rawTime = new KerbalTime(600);
        KerbalTime rawTimeToAlarm = new KerbalTime();
        //Boolean blnRawDate = false;
        //Boolean blnRawInterval = true;
        ///// <summary>
        ///// Layout the raw alarm screen inputs
        ///// </summary>
        int intRawType = 1;
        KerbalTimeStringArray rawEntry = new KerbalTimeStringArray(600);
        private void WindowLayout_AddPane_Raw()
        {
            GUILayout.Label("Enter Raw Time Values...", KACResources.styleAddSectionHeading);

            GUILayout.BeginVertical(KACResources.styleAddFieldAreas);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Time type:", KACResources.styleAddHeading, GUILayout.Width(90));
            if (DrawRadioList(ref intRawType, new string[] { "Date", "Time Interval" }))
            {

            }
            GUILayout.EndHorizontal();

            if (DrawTimeEntry(ref rawEntry, TimeEntryPrecision.Days,"Time:",100,35,15))
            {

            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("UT (raw seconds):", KACResources.styleAddHeading,GUILayout.Width(100));
            strRawUT = GUILayout.TextField(strRawUT, KACResources.styleAddField);
            GUILayout.EndHorizontal();


            GUILayout.EndVertical();
            try
            {
                if (strRawUT != "")
                    rawTime.UT = Convert.ToDouble(strRawUT);
                else
                    rawTime.UT = rawEntry.UT;
        
                //If its an interval add the interval to the current time
                if (intRawType==1)
                    rawTime = new KerbalTime(KACWorkerGameState.CurrentTime.UT + rawTime.UT);

                rawTimeToAlarm = new KerbalTime(rawTime.UT - KACWorkerGameState.CurrentTime.UT);

                //Draw the Add Alarm details at the bottom
                if (DrawAddAlarm(rawTime,null,rawTimeToAlarm))
                {
                    //"VesselID, Name, Message, AlarmTime.UT, Type, Enabled,  HaltWarp, PauseGame, Manuever"
                    String strVesselID = "";
                    if (blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                    Settings.Alarms.Add(new KACAlarm(strVesselID, strAlarmName, strAlarmNotes, rawTime.UT, 0, KACAlarm.AlarmType.Raw, 
                        (AddAction== KACAlarm.AlarmAction.KillWarp), (AddAction== KACAlarm.AlarmAction.PauseGame)));
                    Settings.Save();
                    _ShowAddPane = false;
                }
            }
            catch (Exception)
            {
                GUILayout.Label("Unable to combine all text fields to date", GUILayout.ExpandWidth(true));
            }
        }

        private Boolean DrawAddAlarm(KerbalTime AlarmDate,KerbalTime TimeToEvent,KerbalTime TimeToAlarm)
        {
            Boolean blnReturn = false;
            int intLineHeight = 18;
            //work out the strings
            GUILayout.BeginHorizontal(KACResources.styleAddAlarmArea);
            GUILayout.BeginVertical(GUILayout.Width(100), GUILayout.MaxWidth(100));
            GUILayout.Label(strAlarmEventName + " Date:", KACResources.styleAddHeading, GUILayout.Height(intLineHeight));
            if (AddType != KACAlarm.AlarmType.Raw)
                GUILayout.Label("Time to " + strAlarmEventName + ":", KACResources.styleAddHeading, GUILayout.Height(intLineHeight));

            GUILayout.Label("Time to Alarm:", KACResources.styleAddHeading, GUILayout.Height(intLineHeight));
            GUILayout.EndVertical();
            GUILayout.BeginVertical();

            GUILayout.Label(KerbalTime.PrintInterval(AlarmDate, Settings.TimeFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));
            if (AddType != KACAlarm.AlarmType.Raw)
                GUILayout.Label(KerbalTime.PrintInterval(TimeToEvent, Settings.TimeFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));
            GUILayout.Label(KerbalTime.PrintInterval(TimeToAlarm, Settings.TimeFormat), KACResources.styleContent, GUILayout.Height(intLineHeight));

            GUILayout.EndVertical();
            GUILayout.Space(10);
            int intButtonHeight = 36;
            if (AddType != KACAlarm.AlarmType.Raw) intButtonHeight += 22;
            if (GUILayout.Button("Add Alarm", KACResources.styleButton, GUILayout.Width(90), GUILayout.Height(intButtonHeight)))
            {
                blnReturn=true;
            }
            GUILayout.EndHorizontal();
            return blnReturn;
        }

        ////Variables for Node Alarms screen
        ////String strNodeMargin = "1";
        ///// <summary>
        ///// Screen Layout for adding Alarm from Maneuver Node
        ///// </summary>
        private void WindowLayout_AddPane_Maneuver()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Node Details...", KACResources.styleAddSectionHeading);

            Vessel myVessel = FlightGlobals.ActiveVessel;
            if (myVessel == null)
            {
                GUILayout.Label("No Active Vessel");
            }
            else
            {
                if (!KACWorkerGameState.ManeuverNodeExists)
                {
                    GUILayout.Label("No Maneuver Nodes Found", GUILayout.ExpandWidth(true));
                }
                else
                {
                    Boolean blnFoundNode = false;
                    String strMarginConversion = "";
                    //loop to find the first future node
                    for (int intNode = 0; (intNode < myVessel.patchedConicSolver.maneuverNodes.Count) && !blnFoundNode; intNode++)
                    {
                        KerbalTime nodeTime = new KerbalTime(myVessel.patchedConicSolver.maneuverNodes[intNode].UT);
                        KerbalTime nodeInterval = new KerbalTime(nodeTime.UT - KACWorkerGameState.CurrentTime.UT);

                        KerbalTime nodeAlarm;
                        KerbalTime nodeAlarmInterval;
                        try
                        {
                            nodeAlarm = new KerbalTime(nodeTime.UT - timeMargin.UT);
                            nodeAlarmInterval = new KerbalTime(nodeTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT);
                        }
                        catch (Exception)
                        {
                            nodeAlarm = null;
                            nodeAlarmInterval = null;
                            strMarginConversion = "Unable to Add the Margin Minutes";
                        }

                        if ((nodeTime.UT > KACWorkerGameState.CurrentTime.UT) && strMarginConversion == "")
                        {
                            if (DrawAddAlarm(nodeTime,nodeInterval,nodeAlarmInterval))
                            {
                                //Get a list of all future Maneuver Nodes - thats what the skip does
                                List<ManeuverNode> manNodesToStore = myVessel.patchedConicSolver.maneuverNodes.Skip(intNode).ToList<ManeuverNode>();

                                Settings.Alarms.Add(new KACAlarm(FlightGlobals.ActiveVessel.id.ToString(), strAlarmName, strAlarmNotes, nodeAlarm.UT, timeMargin.UT, KACAlarm.AlarmType.Maneuver,
                                    (AddAction == KACAlarm.AlarmAction.KillWarp), (AddAction == KACAlarm.AlarmAction.PauseGame), manNodesToStore));
                                Settings.Save();
                                _ShowAddPane = false;
                            }
                            blnFoundNode = true;
                        }
                    }

                    if (strMarginConversion != "")
                        GUILayout.Label(strMarginConversion, GUILayout.ExpandWidth(true));
                    else if (!blnFoundNode)
                        GUILayout.Label("No Future Maneuver Nodes Found", GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndVertical();
        }

        private void WindowLayout_AddPane_NodeEvent(Boolean PointFound,Double timeToPoint)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(strAlarmEventName + " Details...", KACResources.styleAddSectionHeading);

            Vessel myVessel = FlightGlobals.ActiveVessel;
            if (myVessel == null)
                GUILayout.Label("No Active Vessel");
            else
            {
                if (!PointFound)
                    GUILayout.Label("No " + strAlarmEventName + " Point Found on current plan", GUILayout.ExpandWidth(true));
                else
                {
                    String strMarginConversion = "";
                    KerbalTime eventTime = new KerbalTime(KACWorkerGameState.CurrentTime.UT+ timeToPoint);
                    KerbalTime eventInterval = new KerbalTime(timeToPoint);

                    KerbalTime eventAlarm;
                    KerbalTime eventAlarmInterval;
                    try
                    {
                        eventAlarm = new KerbalTime(eventTime.UT - timeMargin.UT);
                        eventAlarmInterval = new KerbalTime(eventTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT);
                    }
                    catch (Exception)
                    {
                        eventAlarm = null;
                        eventAlarmInterval = null;
                        strMarginConversion = "Unable to Add the Margin Minutes";
                    }

                    if ((eventTime.UT > KACWorkerGameState.CurrentTime.UT) && strMarginConversion == "")
                    {
                        if (DrawAddAlarm(eventTime, eventInterval, eventAlarmInterval))
                        {
                            KACAlarm newAlarm = new KACAlarm(FlightGlobals.ActiveVessel.id.ToString(), strAlarmName, strAlarmNotes, eventAlarm.UT, timeMargin.UT, AddType,
                                (AddAction == KACAlarm.AlarmAction.KillWarp), (AddAction == KACAlarm.AlarmAction.PauseGame));
                            if (AddType == KACAlarm.AlarmType.AscendingNode || AddType == KACAlarm.AlarmType.DescendingNode)
                                newAlarm.TargetObject = KACWorkerGameState.CurrentVesselTarget;

                            Settings.Alarms.Add(newAlarm);
                            Settings.Save();
                            _ShowAddPane = false;
                        }
                    }
                    else
                    {
                        strMarginConversion="No Future " + strAlarmEventName + "Points found";
                    }

                    if (strMarginConversion != "")
                        GUILayout.Label(strMarginConversion, GUILayout.ExpandWidth(true));
                }
            }

            GUILayout.EndVertical();
        }

        private List<CelestialBody> XferParentBodies = new List<CelestialBody>();
        private List<CelestialBody> XferOriginBodies = new List<CelestialBody>();
        private List<KACXFerTarget> XferTargetBodies = new List<KACXFerTarget>();

        private static int SortByDistance(CelestialBody c1, CelestialBody c2)
        {
            Double f1 = c1.orbit.semiMajorAxis;
            double f2 = c2.orbit.semiMajorAxis;
            //DebugLogFormatted("{0}-{1}", f1.ToString(), f2.ToString());
            return f1.CompareTo(f2);
        }


        private int intXferCurrentParent = 0;
        private int intXferCurrentOrigin = 0;
        private int intXferCurrentTarget = 0;
        //private KerbalTime XferCurrentTargetEventTime;

        private void SetUpXferParents()
        {
            XferParentBodies = new List<CelestialBody>();
            //Build a list of parents - Cant sort this normally as the Sun has no radius - duh!
            foreach (CelestialBody tmpBody in FlightGlobals.Bodies)
            {
                //add any body that has more than 1 child to the parents list
                if (tmpBody.orbitingBodies.Count > 1)
                    XferParentBodies.Add(tmpBody);
            }
        }

        private void SetupXferOrigins()
        {
            //set the possible origins to be all the orbiting bodies around the parent
            XferOriginBodies = new List<CelestialBody>();
            XferOriginBodies = XferParentBodies[intXferCurrentParent].orbitingBodies.OrderBy(b => b.orbit.semiMajorAxis).ToList<CelestialBody>();
            if (intXferCurrentOrigin > XferOriginBodies.Count)
                intXferCurrentOrigin = 0;

            if (AddType== KACAlarm.AlarmType.Transfer || AddType== KACAlarm.AlarmType.TransferModelled) BuildTransferStrings();
        }

        private void SetupXFerTargets()
        {
            XferTargetBodies = new List<KACXFerTarget>();

            //Loop through the Siblings of the origin planet
            foreach (CelestialBody bdyTarget in XferOriginBodies.OrderBy(b => b.orbit.semiMajorAxis))
            {
                //add all the other siblings as target possibilities
                if (bdyTarget != XferOriginBodies[intXferCurrentOrigin])
                {
                    KACXFerTarget tmpTarget = new KACXFerTarget();
                    tmpTarget.Origin = XferOriginBodies[intXferCurrentOrigin];
                    tmpTarget.Target = bdyTarget;
                    //tmpTarget.SetPhaseAngleTarget();
                    //add it to the list
                    XferTargetBodies.Add(tmpTarget);
                }
            }
            if (intXferCurrentTarget > XferTargetBodies.Count)
                intXferCurrentTarget = 0;

            if (AddType == KACAlarm.AlarmType.Transfer || AddType == KACAlarm.AlarmType.TransferModelled) BuildTransferStrings();
        }

        int intAddXferHeight=317;
        int intXferType = 1;
        private void WindowLayout_AddPane_Transfer()
        {
            intAddXferHeight = 317;
            KerbalTime XferCurrentTargetEventTime = null;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Transfers", KACResources.styleHeading);
            //add something here to select the modelled or formula values for Solar orbiting bodies
            if (Settings.XferModelDataLoaded)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Calc by:", KACResources.styleAddHeading);
                if (intXferCurrentParent == 0)
                {
                    //intAddXferHeight += 35;
                    if (DrawRadioList(ref intXferType, "Model", "Formula"))
                    {
                        Settings.XferUseModelData = (intXferType == 0);
                        Settings.Save();
                    }
                }
                else
                {
                    int zero = 0;
                    DrawRadioList(ref zero, "Formula");
                }
            }
            GUILayout.EndHorizontal();
            try
            {
                

                GUILayout.BeginHorizontal();
                GUILayout.Label("Xfer Parent:", KACResources.styleAddHeading, GUILayout.Width(80), GUILayout.Height(20));
                GUILayout.Label(XferParentBodies[intXferCurrentParent].bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Parent Bodies"), KACResources.styleAddXferOriginButton))
                {
                    intXferCurrentParent += 1;
                    if (intXferCurrentParent >= XferParentBodies.Count) intXferCurrentParent = 0;
                    SetupXferOrigins();
                    intXferCurrentOrigin = 0;
                    SetupXFerTargets();
                    BuildTransferStrings();
                    //strAlarmNotesNew = String.Format("{0} Transfer", XferOriginBodies[intXferCurrentOrigin].bodyName);
                }
                GUILayout.Space(34);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Xfer Origin:", KACResources.styleAddHeading, GUILayout.Width(80),GUILayout.Height(20));
                GUILayout.Label(XferOriginBodies[intXferCurrentOrigin].bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Origin Bodies"), KACResources.styleAddXferOriginButton))
                {
                    intXferCurrentOrigin += 1;
                    if (intXferCurrentOrigin >= XferOriginBodies.Count) intXferCurrentOrigin = 0;
                    SetupXFerTargets();
                    BuildTransferStrings();
                    //strAlarmNotesNew = String.Format("{0} Transfer", XferOriginBodies[intXferCurrentOrigin].bodyName);
                }
                if (!Settings.AlarmXferDisplayList)
                    GUILayout.Space(34);
                else
                    if (GUILayout.Button(new GUIContent(KACResources.btnChevronUp, "Hide Full List"), KACResources.styleSmallButton))
                    {
                        Settings.AlarmXferDisplayList = !Settings.AlarmXferDisplayList;
                        Settings.Save();
                    }
                GUILayout.EndHorizontal();

                if (!Settings.AlarmXferDisplayList)
                {
                    //Simple single chosen target
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Xfer Target:", KACResources.styleAddHeading, GUILayout.Width(80), GUILayout.Height(20));
                    GUILayout.Label(XferTargetBodies[intXferCurrentTarget].Target.bodyName, KACResources.styleAddXferName, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                    if (GUILayout.Button(new GUIContent("Change", "Click to cycle through Target Bodies"), KACResources.styleAddXferOriginButton))
                    {
                        intXferCurrentTarget += 1;
                        if (intXferCurrentTarget >= XferTargetBodies.Count) intXferCurrentTarget = 0;
                        SetupXFerTargets();
                        BuildTransferStrings();
                        //strAlarmNotesNew = String.Format("{0} Transfer", XferTargetBodies[intXferCurrentTarget].Target.bodyName);
                    }
                    if (GUILayout.Button(new GUIContent(KACResources.btnChevronDown, "Show Full List"), KACResources.styleSmallButton))
                    {
                        Settings.AlarmXferDisplayList = !Settings.AlarmXferDisplayList;
                        Settings.Save();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal(); 
                    GUILayout.Label("Phase Angle-Current:",  KACResources.styleAddHeading,GUILayout.Width(130));
                    GUILayout.Label(String.Format("{0:0.00}", XferTargetBodies[intXferCurrentTarget].PhaseAngleCurrent), KACResources.styleContent, GUILayout.Width(67));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Phase Angle-Target:", KACResources.styleAddHeading, GUILayout.Width(130));
                    if (intXferCurrentParent != 0 || (!Settings.XferUseModelData && Settings.XferModelDataLoaded))
                    {
                        //formula based
                        GUILayout.Label(String.Format("{0:0.00}", XferTargetBodies[intXferCurrentTarget].PhaseAngleTarget), KACResources.styleContent, GUILayout.Width(67));
                    }
                    else
                    {
                        //this is the modelled data, but only for Kerbol orbiting bodies
                        try
                        {
                            KACXFerModelPoint tmpModelPoint = KACResources.lstXferModelPoints.FirstOrDefault(
                            m => FlightGlobals.Bodies[m.Origin] == XferTargetBodies[intXferCurrentTarget].Origin &&
                                FlightGlobals.Bodies[m.Target] == XferTargetBodies[intXferCurrentTarget].Target &&
                                m.UT >= KACWorkerGameState.CurrentTime.UT);

                            if (tmpModelPoint != null)
                            {
                                GUILayout.Label(String.Format("{0:0.00}", tmpModelPoint.PhaseAngle), KACResources.styleContent, GUILayout.Width(67));
                                XferCurrentTargetEventTime = new KerbalTime(tmpModelPoint.UT);
                            }
                            else
                            {
                                GUILayout.Label("No future model data available for this transfer", KACResources.styleContent, GUILayout.ExpandWidth(true));
                            }
                        }
                        catch (Exception ex)
                        {
                            GUILayout.Label("Unable to determine model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                            DebugLogFormatted("Error determining model data: {0}", ex.Message);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Target", KACResources.styleAddSectionHeading, GUILayout.Width(55));
                    GUILayout.Label(new GUIContent("Phase Angle", "Displayed as \"Current Angle (Target Angle)\""), KACResources.styleAddSectionHeading, GUILayout.Width(105));
                    GUILayout.Label("Time to Transfer", KACResources.styleAddSectionHeading, GUILayout.ExpandWidth(true));
                    //GUILayout.Label("Time to Alarm", KACResources.styleAddSectionHeading, GUILayout.ExpandWidth(true));
                    GUILayout.Label("Add", KACResources.styleAddSectionHeading, GUILayout.Width(30));
                    GUILayout.EndHorizontal();

                    for (int intTarget = 0; intTarget < XferTargetBodies.Count; intTarget++)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(XferTargetBodies[intTarget].Target.bodyName, KACResources.styleAddXferName, GUILayout.Width(55), GUILayout.Height(20));
                        if (intXferCurrentParent != 0 || (!Settings.XferUseModelData && Settings.XferModelDataLoaded))
                        {
                            //formula based
                            String strPhase = String.Format("{0:0.00}({1:0.00})", XferTargetBodies[intTarget].PhaseAngleCurrent, XferTargetBodies[intTarget].PhaseAngleTarget);
                            GUILayout.Label(strPhase, KACResources.styleAddHeading, GUILayout.Width(105), GUILayout.Height(20));
                            GUILayout.Label(KerbalTime.PrintInterval(XferTargetBodies[intTarget].AlignmentTime, Settings.TimeFormat), KACResources.styleAddHeading, GUILayout.ExpandWidth(true), GUILayout.Height(20));
                        }
                        else 
                        { 
                            try
                            {
                                KACXFerModelPoint tmpModelPoint = KACResources.lstXferModelPoints.FirstOrDefault(
                                m => FlightGlobals.Bodies[m.Origin] == XferTargetBodies[intTarget].Origin &&
                                    FlightGlobals.Bodies[m.Target] == XferTargetBodies[intTarget].Target &&
                                    m.UT >= KACWorkerGameState.CurrentTime.UT);
                            
                                if (tmpModelPoint != null)
                                {
                                    String strPhase = String.Format("{0:0.00}({1:0.00})", XferTargetBodies[intTarget].PhaseAngleCurrent, tmpModelPoint.PhaseAngle);
                                    GUILayout.Label(strPhase, KACResources.styleAddHeading, GUILayout.Width(105), GUILayout.Height(20));
                                    KerbalTime tmpTime = new KerbalTime(tmpModelPoint.UT - KACWorkerGameState.CurrentTime.UT);
                                    GUILayout.Label(KerbalTime.PrintInterval(tmpTime, Settings.TimeFormat), KACResources.styleAddHeading, GUILayout.ExpandWidth(true), GUILayout.Height(20));                                

                                    if (intTarget==intXferCurrentTarget)
                                        XferCurrentTargetEventTime = new KerbalTime(tmpModelPoint.UT);
                                }
                                else
                                {
                                    GUILayout.Label("No future model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                                }
                            }
                            catch (Exception ex)
                            {
                                GUILayout.Label("Unable to determine model data", KACResources.styleContent, GUILayout.ExpandWidth(true));
                                DebugLogFormatted("Error determining model data: {0}", ex.Message);
                            }
                        }
                        Boolean blnSelected = (intXferCurrentTarget == intTarget);
                        if (DrawToggle(ref blnSelected, "", KACResources.styleCheckbox, GUILayout.Width(42)))
                        {
                            if (blnSelected)
                            {
                                intXferCurrentTarget = intTarget;
                                BuildTransferStrings();
                            }
                        }

                        GUILayout.EndHorizontal();
                    }

                    intAddXferHeight += -56 + ( XferTargetBodies.Count * 30);
                }

                if (intXferCurrentParent != 0 || (!Settings.XferUseModelData && Settings.XferModelDataLoaded))
                {
                    //Formula based - add new alarm
                    if (DrawAddAlarm(new KerbalTime(KACWorkerGameState.CurrentTime.UT + XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT),
                                    XferTargetBodies[intXferCurrentTarget].AlignmentTime,
                                    new KerbalTime(XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT - timeMargin.UT)))
                    {
                        String strVesselID = "";
                        if (blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                        Settings.Alarms.Add(new KACAlarm(strVesselID, strAlarmName, strAlarmNotes + "\r\n\tOrigin: " + XferTargetBodies[intXferCurrentTarget].Origin.bodyName + "\r\n\tTarget: " + XferTargetBodies[intXferCurrentTarget].Target.bodyName + "\r\n\tMargin: " + new KerbalTime(timeMargin.UT).IntervalString(),
                            (KACWorkerGameState.CurrentTime.UT + XferTargetBodies[intXferCurrentTarget].AlignmentTime.UT - timeMargin.UT), timeMargin.UT, KACAlarm.AlarmType.Transfer,
                            (AddAction == KACAlarm.AlarmAction.KillWarp), (AddAction == KACAlarm.AlarmAction.PauseGame), XferTargetBodies[intXferCurrentTarget]));
                        Settings.Save();
                        _ShowAddPane = false;
                    }
                }
                else
                {
                    //Model based
                    if (XferCurrentTargetEventTime!=null)
                    {
                        if (DrawAddAlarm(XferCurrentTargetEventTime,
                                    new KerbalTime(XferCurrentTargetEventTime.UT - KACWorkerGameState.CurrentTime.UT),
                                    new KerbalTime(XferCurrentTargetEventTime.UT - KACWorkerGameState.CurrentTime.UT - timeMargin.UT)))
                        {
                        String strVesselID = "";
                        if (blnAlarmAttachToVessel) strVesselID = KACWorkerGameState.CurrentVessel.id.ToString();
                        Settings.Alarms.Add(new KACAlarm(strVesselID, strAlarmName, strAlarmNotes,
                            (XferCurrentTargetEventTime.UT - timeMargin.UT), timeMargin.UT, KACAlarm.AlarmType.Transfer,
                            (AddAction == KACAlarm.AlarmAction.KillWarp), (AddAction == KACAlarm.AlarmAction.PauseGame), XferTargetBodies[intXferCurrentTarget]));
                        Settings.Save();
                        _ShowAddPane = false;
                    }
                    }
                    else{
                        GUILayout.Label("Selected a transfer with no event date",GUILayout.ExpandWidth(true));
                    }
                }
            }
            catch (Exception ex)
            {
                if (intXferCurrentTarget >= XferTargetBodies.Count) 
                    intXferCurrentTarget = 0;
                GUILayout.Label("Something weird has happened");
                DebugLogFormatted(ex.Message);
                DebugLogFormatted(ex.StackTrace);
            }

            //intAddXferHeight += intTestheight4;
            GUILayout.EndVertical();
        }

        int AddNotesHeight = 100;
        public void FillAddMessagesWindow(int WindowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Vessel:", KACResources.styleAddHeading);
            String strVesselName = "Not Attached to Vessel";
            if (blnAlarmAttachToVessel) strVesselName = KACWorkerGameState.CurrentVessel.vesselName;
            GUILayout.TextField(strVesselName, KACResources.styleAddFieldGreen);
            GUILayout.Label("Alarm:", KACResources.styleAddHeading);
            strAlarmName = GUILayout.TextField(strAlarmName, KACResources.styleAddField, GUILayout.MaxWidth(184)).Replace("|", "");
            GUILayout.Label("Notes:", KACResources.styleAddHeading);
            strAlarmNotes = GUILayout.TextArea(strAlarmNotes, KACResources.styleAddMessageField,
                                GUILayout.Height(AddNotesHeight), GUILayout.MaxWidth(184)
                                ).Replace("|", ""); 

            GUILayout.EndVertical();
        }
    }
}