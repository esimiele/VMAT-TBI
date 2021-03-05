using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
//using System.Runtime.InteropServices;

namespace VMATTBI_optLoop
{
    //data structure to hold all this crap
    public struct dataContainer
    {
        //data members
        public VMS.TPS.Common.Model.API.Application app;
        public ExternalPlanSetup plan;
        public string id;
        public string MLCmodel;
        public int numOptimizations;
        public double targetVolCoverage;
        public double relativeDose;
        public bool scleroTrial;
        public bool runCoverageCheck;
        public bool oneMoreOpt;
        public bool copyAndSavePlanItr;
        public bool useFlash;
        public List<Tuple<string, string, double, double, int>> optParams;
        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObjSclero;
        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObjGeneral;
        public double threshold;
        public double lowDoseLimit;
        private string targetId;
        //simple method to automatically assign/initialize the above data members
        public void construct(ExternalPlanSetup p, List<Tuple<string, string, double, double, int>> param, double targetNorm, int numOpt, bool sc, bool coverMe, bool unoMas, bool copyAndSave, bool flash, string mlc, VMS.TPS.Common.Model.API.Application a)
        {
            optParams = new List<Tuple<string, string, double, double, int>> { };
            optParams = param;

            plan = p;
            id = plan.Course.Patient.Id;
            numOptimizations = numOpt;
            scleroTrial = sc;

            //what percentage of the target volume should recieve the prescription dose
            targetVolCoverage = targetNorm;
            //MLC model
            MLCmodel = mlc;
            //dose relative to the prescription dose expressed as a percent (used for normalization)
            relativeDose = 100.0;
            //threshold to determine if the dose or the priority should be adjusted for an optimization constraint. This threshold indicates the relative cost, above which, the dose will be decreased for an optimization constraint.
            //Below the threshold, the priority will be increased for an optimization constraint. 
            threshold = 0.15;
            //the low dose limit is used to prevent the algorithm (below) from pushing the dose constraints too low. Basically, if the difference in the calculated dose from the previous optimization and the proposed change in the dose
            //is greater than the prescription dose times the lowDoseLimit, the change is accepted and the dose constraint is modified. Otherwise, the optimization constraint is NOT altered
            lowDoseLimit = 0.1;
            //copy additional optimization loop parameters
            runCoverageCheck = coverMe;
            oneMoreOpt = unoMas;
            copyAndSavePlanItr = copyAndSave;
            useFlash = flash;
            app = a;

            //used to set the correct target id based on whether the user included flash in their optimization
            if (useFlash) targetId = "TS_PTV_FLASH";
            else targetId = "TS_PTV_VMAT";

            //changed PTV_BODY to targetId for the cases where the patient has an appa plan and needs to ts_PTV_VMAT or ts_PTV_FLASH (if flash was used) structure
            planObjSclero = new List<Tuple<string, string, double, double, DoseValuePresentation>>
            {
                //structure, constraint type, dose, relative volume
                //new Tuple<string, string, double, double, DoseValuePresentation>("PTV_Body", "Lower", 800.0, 90.0, DoseValuePresentation.Absolute),
                //new Tuple<string, string, double, double, DoseValuePresentation>("PTV_Body", "Upper", 810.0, 0.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>(targetId, "Lower", 800.0, 90.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>(targetId, "Upper", 810.0, 0.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>("Lungs_Eval", "Mean", 200.0, 0.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>("Kidneys", "Mean", 200.0, 0.0, DoseValuePresentation.Absolute)
            };
            //generic plan objectives for all treatment regiments
            planObjGeneral = new List<Tuple<string, string, double, double, DoseValuePresentation>>
            {
                //structure, constraint type, relative dose, relative volume (unless otherwise specified)
                //note, if the constraint type is "mean", the relative volume value is ignored
                //new Tuple<string, string, double, double, DoseValuePresentation>("PTV_Body", "Lower", 100.0, 90.0, DoseValuePresentation.Relative),
                //new Tuple<string, string, double, double, DoseValuePresentation>("PTV_Body", "Upper", 120.0, 0.0, DoseValuePresentation.Relative),
                //new Tuple<string, string, double, double, DoseValuePresentation>("PTV_Body", "Upper", 110.0, 5.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>(targetId, "Lower", 100.0, 90.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>(targetId, "Upper", 120.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>(targetId, "Upper", 110.0, 5.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Lungs", "Mean", 60.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Lungs-1cm", "Mean", 45.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Kidneys", "Upper", 105.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Kidneys", "Mean", 60.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Bowel", "Upper", 105.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Testes", "Upper", 100.0, 0.0, DoseValuePresentation.Absolute), //these doses are in cGy, not percentage!
                new Tuple<string, string, double, double, DoseValuePresentation>("Testes", "Mean", 25.0, 0.0, DoseValuePresentation.Relative), 
                new Tuple<string, string, double, double, DoseValuePresentation>("Ovaries", "Upper", 100.0, 0.0, DoseValuePresentation.Absolute), //these doses are in cGy, not percentage!
                new Tuple<string, string, double, double, DoseValuePresentation>("Ovaries", "Mean", 25.0, 0.0, DoseValuePresentation.Relative), 
                new Tuple<string, string, double, double, DoseValuePresentation>("Brain-1cm", "Mean", 75.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("Thyroid", "Mean", 75.0, 0.0, DoseValuePresentation.Relative)
            };
        }
    }

    //separate class to help facilitate multithreading
    public class ESAPIworker
    {
        //instance of dataContainer structure to copy the optimization parameters to thread-local memory
        public dataContainer data;
        public readonly Dispatcher _dispatcher;

        //constructor
        public ESAPIworker(dataContainer d)
        {
            //copy optimization parameters from main thread to new thread
            data = d;
            //copy the dispatcher assigned to the main thread (the optimization loop will run on the main thread)
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        //asynchronously execute the supplied task on the main thread
        public void DoWork(Action<dataContainer> a)
        {
            _dispatcher.BeginInvoke(a, data);
        }
    }

    public class optimizationLoop
    {
        //[DllImport("user32.dll")]
        //static extern int FindWindow(string lpClassName, string lpWindowName);
        //[DllImport("user32.dll")]
        //public static extern int SendMessage(int hWnd, uint Msg, int wParam, int lParam);
        //public const int WM_SYSCOMMAND = 0x0112;
        //public const int SC_CLOSE = 0xF060;

        //private void closeWindow()
        //{
        //    // retrieve the handler of the window  
        //    int iHandle = FindWindow("Notepad", "Untitled - Notepad");
        //    if (iHandle > 0)
        //    {
        //        // close the window using API        
        //        SendMessage(iHandle, WM_SYSCOMMAND, SC_CLOSE, 0);
        //    }
        //}
        //[DllImport("user32.dll")]
        //static extern int FindWindow(string lpClassName, string lpWindowName);
        //[DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true)]
        //static extern IntPtr FindWindowByCaption(IntPtr ZeroOnly, string lpWindowName);
        //[DllImport("user32.dll", CharSet = CharSet.Auto)]
        //static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);
        //[DllImport("user32.dll")]
        //private static extern IntPtr GetForegroundWindow();
        //[DllImport("user32.dll")]
        //static extern bool SetForegroundWindow(IntPtr hWnd);
        //[DllImport("user32.dll")]
        //static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        //public const int WM_SYSCOMMAND = 0x0112;
        //public const int SC_CLOSE = 0xF060;
        //const UInt32 WM_CLOSE = 0x0010;

        //data structure to hold the results of the plan evaluation following an optimization
        public struct evalStruct
        {
            //difference between current dose values for each structure in the optimization list and the optimization constraint(s) for that structure
            public List<Tuple<Structure, DVHData, double, double, double, int, int>> diffPlanOpt;
            //same for plan objectives
            public List<Tuple<Structure, DVHData, double, double>> diffPlanObj;
            //vector to hold the updated optimization objectives (to be assigned to the plan)
            public List<Tuple<string, string, double, double, int>> updatedObj;
            //the total cost sum(dose diff^2 * priority) for all structures in the optimization objective vector list
            public double totalCostPlanOpt;
            //same for plan objective vector
            public double totalCostPlanObj;
            //counter to hold the number of added cooler and heater structures to the structure set
            public int numAddedStructs;
            //simple constructure method to initialize the data members. Need to have this here because you can't initialize data members directly within a data structure
            public void construct()
            {
                //vector to hold the results from the optimization for a particular OPTIMIZATION objective
                //structure, dvh data, current dose obj, dose diff^2, cost, current priority, priority difference
                diffPlanOpt = new List<Tuple<Structure, DVHData, double, double, double, int, int>> { };
                //vector to hold the results from the optimization for a particular PLAN objective
                diffPlanObj = new List<Tuple<Structure, DVHData, double, double>> { };
                //vector to hold the updated optimization objectives (following adjustment in the evaluateAndUpdatePlan method)
                updatedObj = new List<Tuple<string, string, double, double, int>> { };
                numAddedStructs = 0;
            }
        }

        public optimizationLoop(ExternalPlanSetup p, List<Tuple<string, string, double, double, int>> param, double targetNorm, int numOpt, bool sc, bool coverMe, bool unoMas, bool copyAndSave, bool flash, string MLC, VMS.TPS.Common.Model.API.Application a)
        {
            //create a new instance of the structure dataContainer and assign the optimization loop parameters entered by the user to the various data members
            dataContainer d = new dataContainer();
            d.construct(p, param, targetNorm, numOpt, sc, coverMe, unoMas, copyAndSave, flash, MLC, a);

            //create a new thread and pass it the data structure created above (it will copy this information to its local thread memory)
            ESAPIworker slave = new ESAPIworker(d);
            //create a new frame (multithreading jargon)
            DispatcherFrame frame = new DispatcherFrame();
            //start the optimization
            //open a new window to run on the newly created thread called "slave"
            //for definition of the syntax used below, google "statement lambda c#"
            RunOnNewThread(() =>
            {
                //pass the progress window the newly created thread and this instance of the optimizationLoop class.
                progressWindow pw = new VMATTBI_optLoop.progressWindow(slave, this);
                pw.ShowDialog();

                //tell the code to hold until the progress window closes.
                frame.Continue = false;
            });

            Dispatcher.PushFrame(frame);
        }

        //method to create the new thread, set the apartment state, set the new thread to be a background thread, and execute the action supplied to this method
        private void RunOnNewThread(Action a)
        {
            Thread t = new Thread(() => a());
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public bool preliminaryChecks(ExternalPlanSetup plan)
        {
            //check if the user assigned the imaging device Id. If not, the optimization will crash with no error
            if (plan.StructureSet.Image.Series.ImagingDeviceId == "")
            {
                MessageBox.Show("Warning! Did you forget to set the imaging device to 'Def_CTScanner'?");
                return true;
            }

            //is the user origin inside the body?
            if (!plan.StructureSet.Image.HasUserOrigin || !(plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "body").IsPointInsideSegment(plan.StructureSet.Image.UserOrigin)))
            {
                MessageBox.Show("Did you forget to set the user origin? \nUser origin is NOT inside body contour! \nPlease fix and try again!");
                return true;
            }

            //are there beams in the plan?
            if (plan.Beams.Count() == 0)
            {
                MessageBox.Show("No beams present in the VMAT TBI plan!");
                return true;
            }

            //check each beam to ensure its z-position is rounded-off to the nearest integer. Ensure the x,y positions are both 0 within +/- 1e-6 cm (need small tolerance due to possible rounding errors in eclipse)
            foreach (Beam b in plan.Beams)
            {
                VVector pos = b.IsocenterPosition;
                pos = plan.StructureSet.Image.DicomToUser(pos, plan);
                //the first argument checks if the rounding error is JUST OVER the nearest integer and the second argument checks if the rounding error is JUST UNDER the nearest integer
                if (pos.x % 1 > 1e-6 && Math.Abs((pos.x % 1) - 1) > 1e-6)
                {
                    MessageBox.Show(String.Format("x, y, z, z % 1, abs((z % 1) - 1), beam id \n{0}, {1}, {2}, {3}, {4}, {5}", pos.x, pos.y, pos.z, pos.z % 1, Math.Abs((pos.z % 1) - 1), b.Id));
                    MessageBox.Show("X position of isocenters are NOT rounded off! Exiting!");
                    return true;
                }
                if (pos.y % 1 > 1e-6 && Math.Abs((pos.y % 1) - 1) > 1e-6)
                {
                    MessageBox.Show(String.Format("x, y, z, z % 1, abs((z % 1) - 1), beam id \n{0}, {1}, {2}, {3}, {4}, {5}", pos.x, pos.y, pos.z, pos.z % 1, Math.Abs((pos.z % 1) - 1), b.Id));
                    MessageBox.Show("Y position of isocenters are NOT rounded off! Exiting!");
                    return true;
                }
                //if pos.z % 1 is not zero, check to see if rounded difference between pos.z % 1 and 1 is > 1 nm (basically check for an internal rounding error in the program). If so, throw an error.
                if (pos.z % 1 > 1e-6 && Math.Abs((pos.z % 1) - 1) > 1e-6)
                {
                    MessageBox.Show(String.Format("x, y, z, z % 1, abs((z % 1) - 1), beam id \n{0}, {1}, {2}, {3}, {4}, {5}", pos.x, pos.y, pos.z, pos.z % 1, Math.Abs((pos.z % 1)-1), b.Id));
                    MessageBox.Show("Z positions of isocenters are NOT rounded-off! Exiting!");
                    return true;
                }
            }

            //ensure the ptv structures are NOT null
            if (plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "ptv_body") == null || plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "ts_ptv_vmat") == null)
            {
                MessageBox.Show("Error! Target structure(s) not found!");
                return true;
            }

            //grab all couch structures including couch surface, rails, etc. Also grab the matchline and spinning manny couch (might not be present depending on the size of the patient)
            IEnumerable<Structure> couch = plan.StructureSet.Structures.Where(x => x.Id.ToLower().Contains("couch"));
            IEnumerable<Structure> rails = plan.StructureSet.Structures.Where(x => x.Id.ToLower().Contains("rail"));
            Structure matchline = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "matchline");
            Structure spinningManny = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "spinmannysurface");
            if(spinningManny == null) spinningManny = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "couchmannysurfac");

            //check to see if the couch and rail structures are present in the structure set. If not, let the user know as an FYI. At this point, the user can choose to stop the optimization loop and add the couch structures
            if (!couch.Any() || !rails.Any())
            {
                confirmUI CUI = new VMATTBI_optLoop.confirmUI();
                CUI.message.Text = String.Format("I didn't found any couch structures in the structure set!") + Environment.NewLine + Environment.NewLine + "Continue?!";
                CUI.ShowDialog();
                if (!CUI.confirm) return true;
            }

            //check if there is a matchline contour. If so, is it empty?
            if (matchline != null && !matchline.IsEmpty)
            {
                //if a matchline contour is present and filled, does the spinning manny couch exist in the structure set? If not, let the user know so they can decide if they want to continue of stop the optimization loop
                if (spinningManny == null || spinningManny.IsEmpty)
                {
                    confirmUI CUI = new VMATTBI_optLoop.confirmUI();
                    CUI.message.Text = String.Format("I found a matchline, but no spinning manny couch or it's empty!") + Environment.NewLine + Environment.NewLine + "Continue?!";
                    CUI.ShowDialog();
                    if (!CUI.confirm) return true;
                }
            }

            //now check if the couch and spinning manny structures are present on the first and last slices of the CT image
            bool checkSupportStruct = false;
            if ((couch.Any() && couch.Where(x => !x.IsEmpty).Any()) && (couch.Where(x => x.GetContoursOnImagePlane(0).Any()).Any() || couch.Where(x => x.GetContoursOnImagePlane(plan.StructureSet.Image.ZSize - 1).Any()).Any())) checkSupportStruct = true;
            if (!checkSupportStruct && (spinningManny != null && !spinningManny.IsEmpty) && (spinningManny.GetContoursOnImagePlane(0).Any() || spinningManny.GetContoursOnImagePlane(plan.StructureSet.Image.ZSize - 1).Any())) checkSupportStruct = true;
            if (checkSupportStruct)
            {
                //couch structures found on first and last slices of CT image. Ask the user if they want to remove the contours for these structures on these image slices
                //We've found that eclipse will throw warning messages after each dose calculation if the couch structures are on the last slices of the CT image. The reason is because a beam could exit the support
                //structure (i.e., the couch) through the end of the couch thus exiting the CT image altogether. Eclipse warns that you are transporting radiation through a structure at the end of the CT image, which
                //defines the world volume (i.e., outside this volume, the radiation transport is killed)
                confirmUI CUI = new VMATTBI_optLoop.confirmUI();
                CUI.message.Text = String.Format("I found couch contours on the first or last slices of the CT image!") + Environment.NewLine + Environment.NewLine + 
                                                 "Do you want to remove them?!" + Environment.NewLine + "(The script will be less likely to throw warnings)";
                CUI.ShowDialog();
                //remove all applicable contours on the first and last CT slices
                if (CUI.confirm)
                {
                    bool recalcDose = false;

                    //If dose has been calculated for this plan, need to clear the dose in this and any and all plans that reference this structure set
                    StructureSet ss = plan.StructureSet;
                    //check to see if this structure set is used in any other calculated plans
                    string message = "The following plans have dose calculated and use the same structure set:" + System.Environment.NewLine;
                    List<ExternalPlanSetup> otherPlans = new List<ExternalPlanSetup> { };
                    foreach (Course c in plan.Course.Patient.Courses)
                    {
                        foreach (ExternalPlanSetup p in c.ExternalPlanSetups)
                        {
                            if (p.IsDoseValid && p.StructureSet == ss)
                            {
                                message += String.Format("Course: {0}, Plan: {1}", c.Id, p.Id) + System.Environment.NewLine;
                                otherPlans.Add(p);
                            }
                        }
                    }

                    if (plan.IsDoseValid || otherPlans.Count > 0)
                    {
                        message += Environment.NewLine + "I need to reset the dose matrix, crop the structures, then re-calculate the dose." + Environment.NewLine + "Continue?!";
                        //8-15-2020 dumbass way around the whole "dose has been calculated, you can't change anything!" issue.
                        CUI = new VMATTBI_optLoop.confirmUI();
                        CUI.message.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
                        CUI.message.Text = message;
                        CUI.ShowDialog();
                        //the user dosen't want to continue
                        if (!CUI.confirm) return true;
                        else
                        {
                            //reset the calculation models for all plans that have calculated dose and share this structure set (inherently wipes the dose)
                            if (otherPlans.Where(x => x != plan).Any()) recalcDose = true;
                            foreach (ExternalPlanSetup p in otherPlans)
                            {
                                string calcModel = p.GetCalculationModel(CalculationType.PhotonVolumeDose);
                                p.ClearCalculationModel(CalculationType.PhotonVolumeDose);
                                p.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
                            }
                        }
                    }
                    foreach (Structure s in couch)
                    {
                        //check to ensure the structure is actually contoured (otherwise you will likely get an error if the structure is null)
                        if (!s.IsEmpty)
                        {
                            s.ClearAllContoursOnImagePlane(0);
                            s.ClearAllContoursOnImagePlane(plan.StructureSet.Image.ZSize - 1);
                        }
                    }
                    foreach (Structure s in rails)
                    {
                        if (!s.IsEmpty)
                        {
                            s.ClearAllContoursOnImagePlane(0);
                            s.ClearAllContoursOnImagePlane(plan.StructureSet.Image.ZSize - 1);
                        }
                    }
                    if (spinningManny != null && !spinningManny.IsEmpty)
                    {
                        spinningManny.ClearAllContoursOnImagePlane(0);
                        spinningManny.ClearAllContoursOnImagePlane(plan.StructureSet.Image.ZSize - 1);
                    }
                    if(recalcDose)
                    {
                        //recalculate dose for each plan that requires it for the current course only!
                        foreach (ExternalPlanSetup p in otherPlans) if (p != plan && p.Course == plan.Course)
                        {
                            p.CalculateDose();    
                        }
                    }
                }
            }

            //ExternalPlanSetup legs_planUpper = plan.Course.ExternalPlanSetups.FirstOrDefault(x => x.Id.ToLower().Contains("legs"));
            //MessageBox.Show("calculating dose");
            //legs_planUpper.CalculateDose();
            //const int nChars = 256;
            //StringBuilder Buff = new StringBuilder(nChars);
            //IntPtr handle = GetForegroundWindow();
            ////MessageBox.Show(GetWindowText(handle, Buff, nChars).ToString());
            //if (GetWindowText(handle, Buff, nChars) > 0)
            //{
            //    MessageBox.Show(Buff.ToString());
            //    //SendMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            //}

            //turn on jaw tracking
            plan.OptimizationSetup.UseJawTracking = true;
            //set auto NTO priority to zero (i.e., shut it off)
            plan.OptimizationSetup.AddAutomaticNormalTissueObjective(0.0);
            //be sure to set the dose value presentation to absolute! This is important for plan evaluation in the evaluateAndUpdatePlan method below
            plan.DoseValuePresentation = DoseValuePresentation.Absolute;
            //return true;
            return false;
        }

//**********************************************************************************************************************************************************************************************************************************************************************************************************************************************************************
// ADJUST THIS CODE IF YOU WANT TO CHANGE HOW THE PROGRAM ADJUSTS THE OPTIMIZATION CONSTRAINTS AFTER EACH ITERATION
//**********************************************************************************************************************************************************************************************************************************************************************************************************************************************************************
        public evalStruct evaluateAndUpdatePlan(ExternalPlanSetup plan, List<Tuple<string, string, double, double, int>> optParams, bool scleroTrial, List<Tuple<string, string, double, double, DoseValuePresentation>> planObjSclero, List<Tuple<string, string, double, double, DoseValuePresentation>> planObjGeneral, double threshold, double lowDoseLimit)
        {
            //create a new data structure to hold the results of the plan quality evaluation
            evalStruct e = new evalStruct();
            e.construct();

            //get current optimization objectives from plan (we could use the optParams list, but we want the actual instances of the OptimizationObjective class so we can get the results from each objective)
            IEnumerable<OptimizationObjective> currentObj = plan.OptimizationSetup.Objectives;
            //get overall plan objectives (either generic of scleroderma trial)
            List<Tuple<string, string, double, double, DoseValuePresentation>> planObj = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
            if (scleroTrial) planObj = planObjSclero;
            else planObj = planObjGeneral;

            //counter to record the number of plan objective met
            int numPass = 0;
            int numComparisons = 0;
            double totalCostPlanObj = 0;
            //loop through all the plan objectives for this case and compare the actual dose to the dose in the plan objective. If we met the constraint, increment numPass. At the end of the loop, if numPass == the number of plan objectives
            //then we have achieved the desired plan quality and can stop the optimization loop
            //string message = "";
            foreach (Tuple<string, string, double, double, DoseValuePresentation> itr in planObj)
            {
                //used to account for the case where there is a template plan objective that is not included in the current case (e.g., testes are not always spared)
                if (plan.StructureSet.Structures.Where(x => x.Id.ToLower() == itr.Item1.ToLower()).Any() && !plan.StructureSet.Structures.First(x => x.Id.ToLower() == itr.Item1.ToLower()).IsEmpty)
                {
                    //similar to code to the foreach loop used to cycle through the optimization parameters
                    Structure s = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == itr.Item1.ToLower());
                    //this statement is difference from the dvh statement in the previous foreach loop because the dose is always expressed as an absolute value in the optimization objectives, but can be either relative or absolute in the plan objectives
                    //(itr.Item5 is the dose representation for this objective)
                    DVHData dvh = plan.GetDVHCumulativeData(s, itr.Item5, VolumePresentation.Relative, 0.1);
                    double diff = 0.0;
                    double cost = 0.0;
                    int optPriority = 0;

                    //NOTE: THERE MAY BE CASES WHERE A STRUCTURE MIGHT HAVE A PLAN OBJECTIVE, BUT NOT AN OPTIMIZATION OBJECTIVE(e.g., ovaries). Check if the structure of interest also has an optimization objective. If so, this indicates the user actually wanted to spare this
                    //structure for this plan and we should increment the number of comparisons counter. In addition, we need to copy the objective priority from the optimization objective if there is one
                    if (optParams.FirstOrDefault(x => x.Item1.ToLower() == itr.Item1.ToLower()) != null)
                    {
                        //If so, do a three-way comparison to find the correct optimization objective for this plan objective (compare based structureId, constraint type, and constraint volume). These three objectives will remain constant
                        //throughout the optimization process whereas the dose constraint will vary
                        IEnumerable<Tuple<string, string, double, double, int>> copyOpt = from p in optParams
                                                                                          where p.Item1.ToLower() == itr.Item1.ToLower()
                                                                                          where p.Item2.ToLower() == itr.Item2.ToLower()
                                                                                          where p.Item4 == itr.Item4
                                                                                          select p;

                        //If the appropriate constraint was found, calculate the cost as the (dose diff)^2 * priority. Also 
                        if (copyOpt.ElementAtOrDefault(0) != null) optPriority = copyOpt.ElementAtOrDefault(0).Item5;
                        //if no exact constraint was found, leave the priority at zero (per Nataliya's instructions)
                        //increment the number of comparisons since an optimization constraint was found
                        numComparisons++;
                    }

                    //similar code as above
                    if (itr.Item2.ToLower() == "upper")
                    {
                        diff = plan.GetDoseAtVolume(s, itr.Item4, VolumePresentation.Relative, itr.Item5).Dose - itr.Item3;
                        //if (plan.GetDoseAtVolume(struRes.Item1, itr.Item4, VolumePresentation.Relative, itr.Item5).Dose <= itr.Item3) numPass++;
                    }
                    else if (itr.Item2.ToLower() == "lower")
                    {
                        diff = itr.Item3 - plan.GetDoseAtVolume(s, itr.Item4, VolumePresentation.Relative, itr.Item5).Dose;
                        //if (plan.GetDoseAtVolume(struRes.Item1, itr.Item4, VolumePresentation.Relative, itr.Item5).Dose >= itr.Item3) numPass++;
                    }
                    else if (itr.Item2.ToLower() == "mean")
                    {
                        diff = dvh.MeanDose.Dose - itr.Item3;
                        //if (struRes.Item2.MeanDose.Dose <= itr.Item3) numPass++;
                    }

                    if (diff <= 0.0)
                    {
                        //objective was met. Increment the counter for the number of objecives met
                        numPass++;
                        diff = 0.0;
                    }
                    else cost = diff * diff * optPriority;

                    //add this comparison to the list and increment the running total of the cost for the plan objectives
                    //MessageBox.Show(String.Format("{0}, {1}, {2}, {3}", s.Id, "something", diff * diff, cost));
                    //message += String.Format("{0}, {1}, {2}, {3}", s.Id, "something", diff * diff, cost) + System.Environment.NewLine;
                    e.diffPlanObj.Add(Tuple.Create(s, dvh, diff * diff, cost));
                    totalCostPlanObj += cost;
                }
            }
            //MessageBox.Show(message);
            e.totalCostPlanObj = totalCostPlanObj;
            if (numPass == numComparisons) return e; //all constraints met, exiting

            //since we didn't meet all of the plan objectives, we now need to evaluate how well the plan compared to the desired plan objectives
            //double to hold the total cost of the optimization
            double totalCostPlanOpt = 0;
            foreach (Tuple<string, string, double, double, int> opt in optParams)
            {
                //get the structure for each optimization object in optParams and its associated DVH
                Structure s = plan.StructureSet.Structures.First(x => x.Id.ToLower() == opt.Item1.ToLower());
                //dose representation in optimization objectives is always absolute!
                DVHData dvh = plan.GetDVHCumulativeData(s, DoseValuePresentation.Absolute, VolumePresentation.Relative, 0.1);

                double diff = 0.0;
                int currentPriority = 0;

                //double currentDose = 0.0;
                //get the list of optimization objectives associated with the current structure associated with opt
                //IEnumerable<OptimizationObjective> obj = currentObj.Where(x => x.StructureId.ToLower() == opt.Item1.ToLower());

                //new method 7-29-2020
                ////string message = "";
                ////loop through each of the optimization objectives assigned to this particular structure (this code was verified on 7-29-2020 by comparing the resulting costs for each structure in a mock plan to the costs obtained using the code from v0.5)
                //foreach (OptimizationObjective o in obj)
                //{
                //    // if (o.GetType().Equals(typeof(OptimizationMeanDoseObjective))) message += String.Format("It's a mean objective! {0}, {1}, {2}\n", (o as OptimizationMeanDoseObjective).StructureId, (o as OptimizationMeanDoseObjective).Dose, (o as OptimizationMeanDoseObjective).Priority);
                //    // else if (o.GetType().Equals(typeof(OptimizationPointObjective))) message += String.Format("It's a point objective! {0}, {1}, {2}, {3}\n", (o as OptimizationPointObjective).StructureId, (o as OptimizationPointObjective).Operator, (o as OptimizationPointObjective).Dose, (o as OptimizationPointObjective).Priority);

                //    if (o.GetType().Equals(typeof(OptimizationMeanDoseObjective)))
                //    {
                //        //this optimization constraint is a mean constraint. NOTE: IT IS ASSUMED THE MAXIMUM NUMBER OF MEAN CONSTRAINTS ASSIGNED TO A STRUCTURE = 1 (it doesn't make sense to assign multiple mean constraints to a structure as they would be conflicting).
                //        if (opt.Item2.ToLower() == "mean")
                //        {
                //            //get the priority and dose values associated with this optimization objective then break the loop
                //            currentPriority = (int)(o as OptimizationMeanDoseObjective).Priority;
                //            currentDose = (o as OptimizationMeanDoseObjective).Dose.Dose;
                //            break;
                //        }
                //    }
                //    else
                //    {
                //        //if it's not an mean objective, then it's a point optimization objective
                //        OptimizationPointObjective op = (OptimizationPointObjective)o;
                //        //it is not uncommon for structures to have multiple point objectives assigned to them. Therefore, we want to verify we have the correction point objective. We do this by ensuring the point objective operator (i.e., upper, lower, exact) is the same as the operator in our optimization parameter list and
                //        //the volume associated with this point objective is the same as the volume in our optimization parameter list. Note the optimization parameter list was passed as an argument to this method.
                //        if (op.Operator.ToString().ToLower() == opt.Item2.ToLower() && op.Volume == opt.Item4)
                //        {
                //            currentPriority = (int)op.Priority;
                //            currentDose = op.Dose.Dose;
                //            break;
                //        }
                //    }
                //}
                //MessageBox.Show(message);

                //calculate the dose difference between the actual plan dose and the optimization dose constraint (separate based on constraint type). If the difference is less than 0, truncate the dose difference to 0
                if (opt.Item2.ToLower() == "upper")
                {
                    //diff = plan.GetDoseAtVolume(s, opt.Item4, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose - currentDose;
                    diff = plan.GetDoseAtVolume(s, opt.Item4, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose - opt.Item3;
                }
                else if (opt.Item2.ToLower() == "lower")
                {
                    //diff = currentDose - plan.GetDoseAtVolume(s, opt.Item4, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
                    diff = opt.Item3 - plan.GetDoseAtVolume(s, opt.Item4, VolumePresentation.Relative, DoseValuePresentation.Absolute).Dose;
                }
                else if (opt.Item2.ToLower() == "mean")
                {
                    //diff = dvh.MeanDose.Dose - currentDose;
                    diff = dvh.MeanDose.Dose - opt.Item3;
                }
                if (diff <= 0.0) diff = 0.0;

                //calculate the cost for this constraint as the dose difference squared times the constraint priority
                double cost = diff * diff * opt.Item5;

                //structure, dvh data, current dose obj, dose diff^2, cost, current priority, priority difference
                //e.diffPlanOpt.Add(Tuple.Create(s, dvh, currentDose, diff * diff, cost, currentPriority, opt.Item5 - currentPriority));

                //7-29-2020 Item7 of this tuple is NOT currently used! 
                //I think the original intent was to compare the current priority against the original priority for the structure. 
                //However, the code was subsequently modified such that optParams was updated at the each of each optimization loop iteration, so the original priority for each optimization objective was lost
                e.diffPlanOpt.Add(Tuple.Create(s, dvh, opt.Item3, diff * diff, cost, opt.Item5, opt.Item5 - currentPriority));
                //add the cost for this constraint to the running total
                totalCostPlanOpt += cost;
            }
            //save the total cost from this optimization
            e.totalCostPlanOpt = totalCostPlanOpt;

            //not all plan objectives were met and now we need to do some investigative work to find out what failed and by how much
            //update optimization parameters based on how each of the structures contained in diffPlanOpt performed
            //string output = "";
            int count = 0;
            foreach (Tuple<Structure, DVHData, double, double, double, int, int> itr in e.diffPlanOpt)
            {
                //placeholders
                double relative_cost = 0.0;
                //assign new objective dose and priority to the current dose and priority
                double newDose = itr.Item3;
                int newPriority = itr.Item6;
                //check to see if objective was met (i.e., was the cost > 0.). If objective was met, adjust nothing and copy the current optimization objective for this structure onto the updatedObj vector
                if (itr.Item5 > 0.0)
                {
                    //objective was not met. Determine what to adjust based on OPTIMIZATION OBJECTIVE parameters (not plan objective parameters)
                    relative_cost = itr.Item5 / totalCostPlanOpt;

                    //do NOT adjust ptv dose constraints, only priorities (the ptv structures are going to have the highest relative cost of all the structures due to the difficulty in covering the entire PTV with 100% of the dose and keeing dMax < 5%)
                    //If we starting adjusting the dose for these constraints, they would quickly escalate out of control, therefore, only adjust their priorities by a small amount
                    if (!itr.Item1.Id.ToLower().Contains("ptv") && (relative_cost >= threshold))
                    {
                        //OAR objective is greater than threshold, adjust dose. Evaluate difference between current actual dose and current optimization parameter setting. Adjust new objective dose by dose difference weighted by the relative cost
                        //=> don't push the dose too low, otherwise the constraints won't make sense. Currently, the lowest dose limit is 10% of the Rx dose (set by adjusting lowDoseLimit)
                        //this equation was (more or less) determined empirically
                        if ((newDose - (Math.Sqrt(itr.Item4) * relative_cost * 2)) >= plan.TotalDose.Dose * lowDoseLimit) newDose -= (Math.Sqrt(itr.Item4) * relative_cost * 2);
                        //else do nothing. This can be changed later to increase the priority instead of doing nothing
                    }
                    else
                    {
                        //OAR objective was less than threshold (or it was a ptv objective), adjust priority
                        //increase OAR objective priority by 100 times the relative cost of this objective
                        //increase PTV objective by 10 times the relative cost (need to have a much lower scaling factor, otherwise it will increase too rapidly)
                        //newPriority += (int)Math.Ceiling(itr.Item7 * relative_cost);
                        double increase = 100 * relative_cost;
                        if (itr.Item1.Id.ToLower().Contains("ptv")) increase /= 10;
                        newPriority += (int)Math.Ceiling(increase);
                    }
                }

                //do NOT update the cooler and heater structure objectives (these will be removed, re-contoured, and re-assigned optimization objectives in the below statements)
                if(!optParams.ElementAt(count).Item1.ToLower().Contains("ts_heater") && !optParams.ElementAt(count).Item1.ToLower().Contains("ts_cooler"))
                    e.updatedObj.Add(Tuple.Create(optParams.ElementAt(count).Item1, optParams.ElementAt(count).Item2, newDose, optParams.ElementAt(count).Item4, newPriority));
                // output += String.Format("{0}, {1}, {2}, {3}, {4}, {5}\n", optParams.ElementAt(count).Item1, optParams.ElementAt(count).Item2, newDose, optParams.ElementAt(count).Item4, newPriority, relative_cost);
                count++;
            }
            //MessageBox.Show(output);

            //update cooler and heater structures for optimization
            //first remove existing structures
            removeCoolHeatStructures(plan);
            //now create new cooler and heating structures
            double doseLevel = 1.10;
            int priority = 80;
            Tuple<string, string, double, double, int> cooler = generateCooler(plan, doseLevel, String.Format("TS_cooler{0}", (int)(doseLevel * 100)), priority);
            if (cooler != null)
            {
                e.updatedObj.Add(cooler);
                e.numAddedStructs++;
            }
            Structure target = null;
            if (plan.OptimizationSetup.Objectives.Where(x => x.StructureId.ToLower() == "ts_ptv_flash").Any()) target = plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_flash");
            else target = plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_vmat");
            double doseLevelLow = 0.90;
            double doseLevelHigh = 1.0;
            priority = 60;
            Tuple<string, string, double, double, int> heater = generateHeater(plan, target, doseLevelLow, doseLevelHigh, String.Format("TS_heater{0}", (int)(doseLevelLow * 100)), priority);
            if (heater != null)
            {
                e.updatedObj.Add(heater);
                e.numAddedStructs++;
            }
            if(plan.Dose.DoseMax3D.Dose / plan.TotalDose.Dose > 1.40 || plan.GetVolumeAtDose(target, new DoseValue(110.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative) > 10.0)
            {
                doseLevelLow = 0.70;
                doseLevelHigh = 0.90;
                priority = 80;
                Tuple<string, string, double, double, int> heater70 = generateHeater(plan, target, doseLevelLow, doseLevelHigh, String.Format("TS_heater{0}", (int)(doseLevelLow * 100)), priority);
                if (heater70 != null)
                {
                    e.updatedObj.Add(heater70);
                    e.numAddedStructs++;
                }
            }
            //return the entire data structure
            return e;
        }

        private void removeCoolHeatStructures(ExternalPlanSetup plan)
        {
            StructureSet s = plan.StructureSet;
            List<Structure> cool = new List<Structure> { };
            foreach (Structure c in s.Structures.Where(x => x.Id.ToLower().Contains("ts_cooler"))) cool.Add(c);
            List<Structure> heater = new List<Structure> { };
            foreach (Structure h in s.Structures.Where(x => x.Id.ToLower().Contains("ts_heater"))) heater.Add(h);
            //now remove the structures
            foreach (Structure c in cool) if (s.CanRemoveStructure(c)) s.RemoveStructure(c); else MessageBox.Show(string.Format("Can't remove cooler: {0}", c.Id));
            foreach (Structure h in heater) if (s.CanRemoveStructure(h)) s.RemoveStructure(h); else MessageBox.Show(string.Format("Can't remove heater: {0}", h.Id));
        }
//**********************************************************************************************************************************************************************************************************************************************************************************************************************************************************************

        private Tuple<string, string, double, double, int> generateCooler(ExternalPlanSetup plan, double doseLevel, string name, int priority)
        {
            //create an empty optiization objective
            Tuple<string, string, double, double, int> cooler = null;
            StructureSet s = plan.StructureSet;
            //grab the relevant dose, dose leve, priority, etc. parameters
            PlanningItemDose d = plan.Dose;
            DoseValue dv = new DoseValue(doseLevel * plan.TotalDose.Dose, DoseValue.DoseUnit.cGy);
            if (s.CanAddStructure("CONTROL", name))
            {
                //add the cooler structure to the structure list and convert the doseLevel isodose volume to a structure. Add this new structure to the list with a max dose objective of Rx * 105% and give it a priority of 80
                Structure coolerStructure = s.AddStructure("CONTROL", name);
                coolerStructure.ConvertDoseLevelToStructure(d, dv);
                cooler = Tuple.Create(name, "Upper", 1.05 * plan.TotalDose.Dose, 0.0, priority);
            }
            return cooler;
        }

        private Tuple<string, string, double, double, int> generateHeater(ExternalPlanSetup plan, Structure target, double doseLevelLow, double doseLevelHigh, string name, int priority)
        {
            //similar to the generateCooler method
            Tuple<string, string, double, double, int> heater = null;
            Structure heaterStructure = null;
            StructureSet s = plan.StructureSet;
            PlanningItemDose d = plan.Dose;
            DoseValue dv = new DoseValue(doseLevelLow * plan.TotalDose.Dose, DoseValue.DoseUnit.cGy);
            if (s.CanAddStructure("CONTROL", name))
            {
                //Structure target = s.Structures.First(x => x.Id.ToLower() == "ts_ptv_vmat");
                //segment lower isodose volume
                heaterStructure = s.AddStructure("CONTROL", name);
                heaterStructure.ConvertDoseLevelToStructure(d, dv);
                //segment higher isodose volume
                Structure dummy = s.AddStructure("CONTROL", "dummy");
                //dummy.ConvertDoseLevelToStructure(d, plan.TotalDose);
                dummy.ConvertDoseLevelToStructure(d, new DoseValue(doseLevelHigh * plan.TotalDose.Dose, DoseValue.DoseUnit.cGy));
                //boolean the lower isodose volume with ts_PTV_VMAT (we only want the lower isodose volumes that overlap with the target) onto the heater structure
                heaterStructure.SegmentVolume = heaterStructure.And(target.SegmentVolume.Margin(0.0));
                //subtract the higher isodose volume from the heater structure and assign it to the heater structure. This is the heater structure that will be used for optimization. Create a new optimization objective for this tunning structure
                heaterStructure.SegmentVolume = heaterStructure.Sub(dummy.SegmentVolume.Margin(0.0));
                heater = Tuple.Create(name, "Lower", plan.TotalDose.Dose, 100.0, priority);
                //clean up
                s.RemoveStructure(dummy);
                //only keep the overlapping regions of the heater structure with the taget structure
                heaterStructure.SegmentVolume = heaterStructure.And(target.Margin(0.0));
            }
            return heater;
        }

        public void updateConstraints(List<Tuple<string, string, double, double, int>> obj, ExternalPlanSetup plan)
        {
            //remove all existing optimization constraints
            foreach (OptimizationObjective o in plan.OptimizationSetup.Objectives) plan.OptimizationSetup.RemoveObjective(o);
            //assign the new optimization constraints (passed as an argument to this method)
            foreach (Tuple<string, string, double, double, int> opt in obj)
            {
                if (opt.Item2.ToLower() == "upper") plan.OptimizationSetup.AddPointObjective(plan.StructureSet.Structures.First(x => x.Id == opt.Item1), OptimizationObjectiveOperator.Upper, new DoseValue(opt.Item3, DoseValue.DoseUnit.cGy), opt.Item4, opt.Item5);
                else if (opt.Item2.ToLower() == "lower") plan.OptimizationSetup.AddPointObjective(plan.StructureSet.Structures.First(x => x.Id == opt.Item1), OptimizationObjectiveOperator.Lower, new DoseValue(opt.Item3, DoseValue.DoseUnit.cGy), opt.Item4, opt.Item5);
                else if (opt.Item2.ToLower() == "mean") plan.OptimizationSetup.AddMeanDoseObjective(plan.StructureSet.Structures.First(x => x.Id == opt.Item1), new DoseValue(opt.Item3, DoseValue.DoseUnit.cGy), opt.Item5);
                else if (opt.Item2.ToLower() == "exact") MessageBox.Show("Script not setup to handle exact dose constraints!");
                else MessageBox.Show("Constraint type not recognized!");
            }
        }

        public void normalizePlan(ExternalPlanSetup plan, double relativeDose, double targetVolCoverage, bool useFlash)
        {
            //in demo mode, dose might not be calculated for the plan
            if (!plan.IsDoseValid) return;
            //how to normalize a plan in the ESAPI workspace:
            //reference: https://github.com/VarianAPIs/Varian-Code-Samples/blob/master/webinars%20%26%20workshops/Research%20Symposium%202015/Eclipse%20Scripting%20API/Projects/AutomatedPlanningDemo/PlanGeneration.cs
            Structure target;
            if (!useFlash) target = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "ts_ptv_vmat");
            else target = plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "ts_ptv_flash");
            plan.PlanNormalizationValue = 100.0;
            //absolute dose
            double RxDose = plan.TotalDose.Dose;
            //construct a DoseValue from RxDose
            DoseValue dv = new DoseValue(relativeDose * RxDose / 100, DoseValue.DoseUnit.cGy);
            //get current coverage of the RxDose
            double coverage = plan.GetVolumeAtDose(target, dv, VolumePresentation.Relative);
            //MessageBox.Show(String.Format("{0}, {1}", dv, coverage));
            
            //if the current coverage doesn't equal the desired coverage, then renormalize the plan
            if (coverage != targetVolCoverage)
            {
                //get the dose that does cover the targetVolCoverage of the target volume and scale the dose distribution by the ratio of that dose to the relative prescription dose
                dv = plan.GetDoseAtVolume(target, targetVolCoverage, VolumePresentation.Relative, DoseValuePresentation.Absolute);
                plan.PlanNormalizationValue = 100.0 * dv.Dose / (relativeDose * RxDose / 100);
                //MessageBox.Show(String.Format("{0}, {1}, {2}", dv, plan.PlanNormalizationValue, plan.Dose.DoseMax3D.Dose));
            }
        }
    }
}
