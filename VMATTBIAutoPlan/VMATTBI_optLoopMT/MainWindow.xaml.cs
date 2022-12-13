using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using Microsoft.Win32;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Reflection;
using VMATTBIautoPlan;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMATTBI_optLoop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// MAIN PARAMETERS FOR THIS CLASS AND ALL OTHER CLASSES IN THIS APPLICATION.
        /// ADJUST THESE PARAMETERS TO YOUR TASTE
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //configuration file
        string configFile = "";
        //point this to the directory holding the documentation files
        string documentationPath = @"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\Research\VMAT_TBI\documentation\";
        //default number of optimizations to perform
        string defautlNumOpt = "3";
        //default plan normalization (i.e., PTV100% = 90%) 
        string defaultPlanNorm = "90";
        //run coverage check
        bool runCoverageCheckOption = false;
        //run additional optimization option
        bool runAdditionalOptOption = true;
        //copy and save each optimized plan
        bool copyAndSaveOption = false;
        //is demo
        bool demo = false;
        //check for spinning manny couch
        bool checkSpinningManny = true;
        //log file directory
        string logFilePath = @"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\Research\VMAT_TBI\log_files";
        //decision threshold
        double threshold = 0.15;
        //lower dose limit
        double lowDoseLimit = 0.1;


        //changed PTV_BODY to targetId for the cases where the patient has an appa plan and needs to ts_PTV_VMAT or ts_PTV_FLASH (if flash was used) structure
        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObjSclero = new List<Tuple<string, string, double, double, DoseValuePresentation>>
            {
                //structure, constraint type, dose, relative volume
                //"<targetId>" will be overwritten with the actual Id of the target (depends if flash was used) 
                new Tuple<string, string, double, double, DoseValuePresentation>("<targetId>", "Lower", 800.0, 90.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>("<targetId>", "Upper", 810.0, 0.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>("Lungs_Eval", "Mean", 200.0, 0.0, DoseValuePresentation.Absolute),
                new Tuple<string, string, double, double, DoseValuePresentation>("Kidneys", "Mean", 200.0, 0.0, DoseValuePresentation.Absolute)
            };
        //generic plan objectives for all treatment regiments
        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObjGeneral = new List<Tuple<string, string, double, double, DoseValuePresentation>>
            {
                //structure, constraint type, relative dose, relative volume (unless otherwise specified)
                //note, if the constraint type is "mean", the relative volume value is ignored
                new Tuple<string, string, double, double, DoseValuePresentation>("<targetId>", "Lower", 100.0, 90.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("<targetId>", "Upper", 120.0, 0.0, DoseValuePresentation.Relative),
                new Tuple<string, string, double, double, DoseValuePresentation>("<targetId>", "Upper", 110.0, 5.0, DoseValuePresentation.Relative),
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

        public List<Tuple<string, string, double, double, DoseValuePresentation>> planObj = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
        public List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>> requestedTSstructures = new List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>>
        {
            new Tuple<string,double,double,double,int,List<Tuple<string, double, string, double>>>("TS_cooler110",110.0,0.0,0.0,80,new List<Tuple<string, double, string, double>>{ }),
            new Tuple<string,double,double,double,int,List<Tuple<string, double, string, double>>>("TS_heater90",90.0,100.0,0.0,60,new List<Tuple<string, double, string, double>>{ }),
            new Tuple<string,double,double,double,int,List<Tuple<string, double, string, double>>>("TS_cooler70",70.0,90.0,0.0,80,new List<Tuple<string, double, string, double>>{new Tuple<string, double, string, double>("Dmax",0.0,">",140), new Tuple<string, double, string, double>("V",110.0,">",10)}),
        };
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        VMS.TPS.Common.Model.API.Application app = null;
        Patient pi = null;
        bool firstOptStruct = true;
        int clearOptBtnCounter = 0;
        bool scleroTrial = false;
        bool runCoverageCheck = false;
        bool runOneMoreOpt = false;
        bool copyAndSavePlanItr = false;
        bool useFlash = false;
        ExternalPlanSetup VMATTBIPlan = null;

        public MainWindow()
        {
            InitializeComponent();
            try { app = VMS.TPS.Common.Model.API.Application.CreateApplication(); }
            catch (Exception except) { MessageBox.Show(String.Format("Warning! Could not generate Aria application instance because: {0}", except.Message)); }
            if (File.Exists(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\configuration\\VMAT_TBI_config.ini"))
            {
                if (!loadConfigurationSettings(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\configuration\\VMAT_TBI_config.ini")) displayConfigurationParameters();
            }
            else MessageBox.Show("No configuration file found! Loading default settings!");
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "VMAT_TBI_guide.pdf");
        }

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "TBI_executable_quickStart_guide.pdf");
        }

        private void displayConfigurationParameters()
        {
            configTB.Text = "";
            configTB.Text = String.Format("{0}", DateTime.Now.ToString()) + System.Environment.NewLine;
            if (configFile != "") configTB.Text += String.Format("Configuration file: {0}", configFile) + System.Environment.NewLine + System.Environment.NewLine;
            else configTB.Text += String.Format("Configuration file: none") + System.Environment.NewLine + System.Environment.NewLine;
            configTB.Text += String.Format("Documentation path: {0}", documentationPath) + System.Environment.NewLine + System.Environment.NewLine;
            configTB.Text += String.Format("Log file path: {0}", logFilePath) + System.Environment.NewLine + System.Environment.NewLine;
            configTB.Text += String.Format("Default run parameters:") + System.Environment.NewLine;
            configTB.Text += String.Format("Demo mode: {0}", demo) + System.Environment.NewLine;
            configTB.Text += String.Format("Check for Spinning Manny couchtop: {0}",checkSpinningManny) + System.Environment.NewLine;
            configTB.Text += String.Format("Run coverage check: {0}", runCoverageCheckOption) + System.Environment.NewLine;
            configTB.Text += String.Format("Run additional optimization: {0}", runAdditionalOptOption) + System.Environment.NewLine;
            configTB.Text += String.Format("Copy and save each optimized plan: {0}", copyAndSaveOption) + System.Environment.NewLine;
            configTB.Text += String.Format("Plan normalization: {0}% (i.e., PTV V100% = {0}%)", defaultPlanNorm) + System.Environment.NewLine;
            configTB.Text += String.Format("Decision threshold: {0}", threshold) + System.Environment.NewLine;
            configTB.Text += String.Format("Relative lower dose limit: {0}", lowDoseLimit) + System.Environment.NewLine + System.Environment.NewLine;
            
            configTB.Text += "---------------------------------------------------------------------------" + System.Environment.NewLine;
            configTB.Text += String.Format("Scleroderma trial plan objectives:") + System.Environment.NewLine;
            configTB.Text += String.Format(" {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -9} |", "structure Id", "constraint type", "dose", "volume (%)", "dose type") + System.Environment.NewLine;
            foreach (Tuple<string, string, double, double, DoseValuePresentation> itr in planObjSclero) configTB.Text += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-9} |" + System.Environment.NewLine, itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);
            configTB.Text += System.Environment.NewLine;
            configTB.Text += System.Environment.NewLine;

            configTB.Text += "---------------------------------------------------------------------------" + System.Environment.NewLine;
            configTB.Text += String.Format("General plan objectives:") + System.Environment.NewLine;
            configTB.Text += String.Format(" {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -9} |", "structure Id", "constraint type", "dose", "volume (%)", "dose type") + System.Environment.NewLine;
            foreach (Tuple<string, string, double, double, DoseValuePresentation> itr in planObjGeneral) configTB.Text += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-9} |" + System.Environment.NewLine, itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);
            configTB.Text += System.Environment.NewLine;
            
            configTB.Text += "---------------------------------------------------------------------------" + System.Environment.NewLine;
            configTB.Text += String.Format("Requested tuning structures:") + System.Environment.NewLine;
            configTB.Text += String.Format(" {0, -15} | {1, -9} | {2, -10} | {3, -5} | {4, -8} | {5, -10} |", "structure Id", "low D (%)", "high D (%)", "V (%)", "priority", "constraint") + System.Environment.NewLine;
            foreach (Tuple<string, double,double,double,int,List<Tuple<string,double,string,double>>> itr in requestedTSstructures)
            {
                configTB.Text += String.Format(" {0, -15} | {1, -9:N1} | {2,-10:N1} | {3,-5:N1} | {4,-8} |", itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);
                if (!itr.Item6.Any()) configTB.Text += String.Format(" {0,-10} |", "none") + System.Environment.NewLine;
                else 
                {
                    int count = 0;
                    foreach (Tuple<string, double, string, double> itr1 in itr.Item6)
                    {
                        if (count == 0)
                        {
                            if (itr1.Item1.Contains("Dmax")) configTB.Text += String.Format(" {0,-10} |", String.Format("{0}{1}{2}%", itr1.Item1, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                            else if(itr1.Item1.Contains("V")) configTB.Text += String.Format(" {0,-10} |", String.Format("{0}{1}%{2}{3}%", itr1.Item1, itr1.Item2, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                            else configTB.Text += String.Format(" {0,-10} |", String.Format("{0}", itr1.Item1)) + System.Environment.NewLine;
                        }
                        else
                        {
                            if (itr1.Item1.Contains("Dmax")) configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}{2}%", itr1.Item1, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                            else if (itr1.Item1.Contains("V")) configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}%{2}{3}%", itr1.Item1, itr1.Item2, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                            else configTB.Text += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}", itr1.Item1)) + System.Environment.NewLine;
                        }
                        count++;
                    }
                }
            }
            configScroller.ScrollToTop();
        }

        private void targetNormInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("This is used to set the plan normalization. What percentage of the PTV volume should recieve the prescription dose?");
        }

        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            if (app == null) return;
            //open the patient with the user-entered MRN number
            string pat_mrn = MRN.Text;
            clearEverything();
            try
            {
                app.ClosePatient();
                pi = app.OpenPatientById(pat_mrn);
                if(pi == null) { MessageBox.Show(String.Format("No Patient found with MRN: {0}! Please double check entered MRN and try again!", pat_mrn)); return; }

                VMATTBIPlan = getPlan();
                if (VMATTBIPlan == null) { MessageBox.Show(String.Format("No plans named _VMAT TBI found for patient: {0}! Close this script, and run the plugin script to generate the _VMAT TBI plan for optimization!", pat_mrn)); return; }

                //populate the optimization stackpanel with the optimization parameters that were stored in the VMAT TBI plan
                populateOptimizationTab(VMATTBIPlan);
                //populate the prescription text boxes with the prescription stored in the VMAT TBI plan
                populateRx(VMATTBIPlan);
                //set the default parameters for the optimization loop
                runCoverageCk.IsChecked = runCoverageCheckOption;
                numOptLoops.Text = defautlNumOpt;
                runAdditionalOpt.IsChecked = runAdditionalOptOption;
                copyAndSave.IsChecked = copyAndSaveOption;
                targetNormTB.Text = defaultPlanNorm;
            }
            catch { MessageBox.Show("No such patient exists!"); }
        }

        private void clearEverything()
        {
            //clear all existing content from the main window
            dosePerFx.Text = numFx.Text = Rx.Text = numOptLoops.Text = "";
            firstOptStruct = true;
            opt_parameters.Children.Clear();
            clearOptBtnCounter = 0;
            scleroTrial = false;
        }

        private void populateRx(ExternalPlanSetup plan)
        {
            //populate the prescription text boxes
            dosePerFx.Text = plan.DosePerFraction.Dose.ToString();
            numFx.Text = plan.NumberOfFractions.ToString();
            Rx.Text = plan.TotalDose.Dose.ToString();
            //if the dose per fraction and number of fractions equal 200 cGy and 4, respectively, then this is a scleroderma trial patient. This information will be passed to the optimization loop
            if (plan.DosePerFraction.Dose == 200.0 && plan.NumberOfFractions == 4) scleroTrial = true;
        }

        private void populateOptimizationTab(ExternalPlanSetup plan)
        {
            //grab the optimization constraints in the existing VMAT TBI plan and display them to the user
            List<Tuple<string, string, double, double, int>> defaultList = new List<Tuple<string, string, double, double, int>> { };
            IEnumerable<OptimizationObjective> obj = plan.OptimizationSetup.Objectives;
            OptimizationPointObjective pt;
            OptimizationMeanDoseObjective mean;
            foreach (OptimizationObjective o in obj)
            {
                //do NOT include any cooler or heater tuning structures in the list
                if (!o.StructureId.ToLower().Contains("ts_cooler") && !o.StructureId.ToLower().Contains("ts_heater"))
                {
                    if (o.GetType() == typeof(OptimizationPointObjective))
                    {
                        pt = (o as OptimizationPointObjective);
                        defaultList.Add(Tuple.Create(pt.StructureId, pt.Operator.ToString(), pt.Dose.Dose, pt.Volume, (int)pt.Priority));

                    }
                    else if (o.GetType() == typeof(OptimizationMeanDoseObjective))
                    {
                        mean = (o as OptimizationMeanDoseObjective);
                        defaultList.Add(Tuple.Create(mean.StructureId, "Mean", mean.Dose.Dose, 0.0, (int)mean.Priority));
                    }
                }
            }

            //clear the current list of optimization constraints and ones obtained from the plan to the user
            clearAllOptimizationStructs();
            if (obj.Count() > 0) add_opt_volumes(plan.StructureSet, defaultList);
        }

        private void getOptFromPlan_Click(object sender, RoutedEventArgs e)
        {
            if (app == null) return;
            if (VMATTBIPlan == null) return;
            populateOptimizationTab(VMATTBIPlan);
        }

        private void startOpt_Click(object sender, RoutedEventArgs e)
        {
            if (app == null) return;
            //start the optimization loop
            //checks
            if (opt_parameters.Children.Count == 0)
            {
                MessageBox.Show("No optimization parameters present to assign to the VMAT plan!");
                return;
            }
            if (!int.TryParse(numOptLoops.Text, out int numOptimizations))
            {
                MessageBox.Show("Error! Invalid input for number of optimization loops! \nFix and try again.");
                return;
            }
            //get an instnace of the VMAT TBI plan
            if (VMATTBIPlan == null) return;

            if(!double.TryParse(targetNormTB.Text, out double planNorm))
            {
                MessageBox.Show("Error! Target normalization is NaN \nFix and try again.");
                return;
            }
            if(planNorm < 0.0 || planNorm > 100.0)
            {
                MessageBox.Show("Error! Target normalization is is either < 0% or > 100% \nExiting!");
                return;
            }

            //get constraints
            //same code as from the binary plug in
            List<Tuple<string, string, double, double, int>> optParametersList = new List<Tuple<string, string, double, double, int>> { };
            string structure = "";
            string constraintType = "";
            double dose = -1.0;
            double vol = -1.0;
            int priority = -1;
            int txtbxNum = 1;
            bool firstCombo = true;
            bool headerObj = true;
            foreach (object obj in opt_parameters.Children)
            {
                if (!headerObj)
                {
                    foreach (object obj1 in ((StackPanel)obj).Children)
                    {
                        if (obj1.GetType() == typeof(ComboBox))
                        {
                            if (firstCombo)
                            {
                                structure = (obj1 as ComboBox).SelectedItem.ToString();
                                firstCombo = false;
                            }
                            else constraintType = (obj1 as ComboBox).SelectedItem.ToString();
                        }
                        else if (obj1.GetType() == typeof(TextBox))
                        {
                            if (!string.IsNullOrWhiteSpace((obj1 as TextBox).Text))
                            {
                                if (txtbxNum == 1) double.TryParse((obj1 as TextBox).Text, out vol);
                                else if (txtbxNum == 2) double.TryParse((obj1 as TextBox).Text, out dose);
                                else int.TryParse((obj1 as TextBox).Text, out priority);
                            }
                            txtbxNum++;
                        }
                    }
                    if (structure == "--select--" || constraintType == "--select--")
                    {
                        MessageBox.Show("Error! \nStructure or Sparing Type not selected! \nSelect an option and try again");
                        return;
                    }
                    else if (dose == -1.0 || vol == -1.0 || priority == -1.0)
                    {
                        MessageBox.Show("Error! \nDose, volume, or priority values are invalid! \nEnter new values and try again");
                        return;
                    }
                    else optParametersList.Add(Tuple.Create(structure, constraintType, dose, vol, priority));
                    firstCombo = true;
                    txtbxNum = 1;
                    dose = -1.0;
                    vol = -1.0;
                    priority = -1;
                }
                else headerObj = false;
            }

            if (optParametersList.Where(x => x.Item1.ToLower().Contains("flash")).Any()) useFlash = true;

            //does the user want to run the initial dose coverage check?
            runCoverageCheck = runCoverageCk.IsChecked.Value;
            //does the user want to run one additional optimization to reduce hotspots?
            runOneMoreOpt = runAdditionalOpt.IsChecked.Value;
            //does the user want to copy and save each plan after it's optimized (so the user can choose between the various plans)?
            copyAndSavePlanItr = copyAndSave.IsChecked.Value;

            //construct the actual plan objective array
            ConstructPlanObjectives();

            //start the optimization loop (all saving to the database is performed in the progressWindow class)
            pi.BeginModifications();
            optimizationLoop optLoop = new optimizationLoop(VMATTBIPlan, optParametersList, planObj, requestedTSstructures, planNorm, numOptimizations, runCoverageCheck, runOneMoreOpt, copyAndSavePlanItr, useFlash, threshold, lowDoseLimit, demo, checkSpinningManny, logFilePath, app);
        }

        private void ConstructPlanObjectives()
        {
            List<Tuple<string, string, double, double, DoseValuePresentation>> temp = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
            if (scleroTrial) temp = planObjSclero;
            else temp = planObjGeneral;
            foreach(Tuple<string,string,double,double,DoseValuePresentation> obj in temp)
            {
                if(obj.Item1 == "<targetId>")
                {
                    if(useFlash) planObj.Add(Tuple.Create("TS_PTV_FLASH", obj.Item2, obj.Item3, obj.Item4, obj.Item5)); 
                    else planObj.Add(Tuple.Create("TS_PTV_VMAT", obj.Item2, obj.Item3, obj.Item4, obj.Item5)); 
                }
                else planObj.Add(Tuple.Create(obj.Item1, obj.Item2, obj.Item3, obj.Item4, obj.Item5));
            }
        }

        private void add_constraint_Click(object sender, RoutedEventArgs e)
        {
            if (app == null) return;
            //add a blank contraint to the list
            if (VMATTBIPlan != null)
            {
                add_opt_volumes(VMATTBIPlan.StructureSet, new List<Tuple<string, string, double, double, int>> { Tuple.Create("--select--", "--select--", 0.0, 0.0, 0) });
                optParamScroller.ScrollToBottom();
            }
        }

        private ExternalPlanSetup getPlan()
        {
            List<ExternalPlanSetup> plans = new List<ExternalPlanSetup> { };
            ExternalPlanSetup thePlan = null;
            //grab an instance of the VMAT TBI plan. Return null if it isn't found
            if (pi == null) return null;
            List<Course> courses = pi.Courses.ToList();
            if (!courses.Any()) MessageBox.Show("No courses attached to patient!");
            else
            {
                //look for plan with Id = '_VMAT TBI'
                plans = courses.SelectMany(x => x.ExternalPlanSetups).Where(x => x.Id.ToLower() == "_vmat tbi").ToList();
                if (!plans.Any()) MessageBox.Show("No plans named _VMAT TBI in any course!");
                else if (plans.Count > 1)
                {
                    //do something
                    selectItem selectCourse = new selectItem();
                    selectCourse.title.Text = "Multiple plans named '_VMAT TBI' found!\nPlease select a course!";
                    foreach (ExternalPlanSetup itr in plans) selectCourse.itemCombo.Items.Add(itr.Course.Id);
                    selectCourse.itemCombo.SelectedIndex = 0;
                    selectCourse.ShowDialog();
                    if (selectCourse.confirm) thePlan = plans.FirstOrDefault(x => x.Course.Id == selectCourse.itemCombo.SelectedItem.ToString());
                }
                else thePlan = plans.First();
            }
            return thePlan;
        }

        private void add_opt_header()
        {
            //same code from the binary plug in
            StackPanel sp1 = new StackPanel();
            sp1.Height = 30;
            sp1.Width = opt_parameters.Width;
            sp1.Orientation = Orientation.Horizontal;
            sp1.Margin = new Thickness(15, 0, 5, 5);

            Label strName = new Label();
            strName.Content = "Structure";
            strName.HorizontalAlignment = HorizontalAlignment.Center;
            strName.VerticalAlignment = VerticalAlignment.Top;
            strName.Width = 100;
            strName.FontSize = 14;
            strName.Margin = new Thickness(27, 0, 0, 0);

            Label spareType = new Label();
            spareType.Content = "Constraint";
            spareType.HorizontalAlignment = HorizontalAlignment.Center;
            spareType.VerticalAlignment = VerticalAlignment.Top;
            spareType.Width = 90;
            spareType.FontSize = 14;
            spareType.Margin = new Thickness(12, 0, 0, 0);

            Label volLabel = new Label();
            volLabel.Content = "V (%)";
            volLabel.HorizontalAlignment = HorizontalAlignment.Center;
            volLabel.VerticalAlignment = VerticalAlignment.Top;
            volLabel.Width = 60;
            volLabel.FontSize = 14;
            volLabel.Margin = new Thickness(18, 0, 0, 0);

            Label doseLabel = new Label();
            doseLabel.Content = "D (cGy)";
            doseLabel.HorizontalAlignment = HorizontalAlignment.Center;
            doseLabel.VerticalAlignment = VerticalAlignment.Top;
            doseLabel.Width = 60;
            doseLabel.FontSize = 14;
            doseLabel.Margin = new Thickness(3, 0, 0, 0);

            Label priorityLabel = new Label();
            priorityLabel.Content = "Priority";
            priorityLabel.HorizontalAlignment = HorizontalAlignment.Center;
            priorityLabel.VerticalAlignment = VerticalAlignment.Top;
            priorityLabel.Width = 65;
            priorityLabel.FontSize = 14;
            priorityLabel.Margin = new Thickness(13, 0, 0, 0);

            sp1.Children.Add(strName);
            sp1.Children.Add(spareType);
            sp1.Children.Add(volLabel);
            sp1.Children.Add(doseLabel);
            sp1.Children.Add(priorityLabel);
            opt_parameters.Children.Add(sp1);

            firstOptStruct = false;
        }

        private void add_opt_volumes(StructureSet selectedSS, List<Tuple<string, string, double, double, int>> defaultList)
        {
            //same code from binary plug in
            if (firstOptStruct) add_opt_header();
            for (int i = 0; i < defaultList.Count; i++)
            {
                StackPanel sp = new StackPanel();
                sp.Height = 30;
                sp.Width = opt_parameters.Width;
                sp.Orientation = Orientation.Horizontal;
                sp.Margin = new Thickness(5);

                ComboBox opt_str_cb = new ComboBox();
                opt_str_cb.Name = "opt_str_cb";
                opt_str_cb.Width = 130;
                opt_str_cb.Height = sp.Height - 5;
                opt_str_cb.HorizontalAlignment = HorizontalAlignment.Left;
                opt_str_cb.VerticalAlignment = VerticalAlignment.Top;
                opt_str_cb.Margin = new Thickness(10, 5, 0, 0);

                opt_str_cb.Items.Add("--select--");
                //this code is used to fix the issue where the structure exists in the structure set, but doesn't populate as the default option in the combo box.
                int index = 0;
                //j is initially 1 because we already added "--select--" to the combo box 
                int j = 1;
                foreach (Structure s in selectedSS.Structures)
                {
                    opt_str_cb.Items.Add(s.Id);
                    if (s.Id.ToLower() == defaultList[i].Item1.ToLower()) index = j;
                    j++;
                }
                opt_str_cb.SelectedIndex = index;
                opt_str_cb.HorizontalContentAlignment = HorizontalAlignment.Center;
                sp.Children.Add(opt_str_cb);

                ComboBox constraint_cb = new ComboBox();
                constraint_cb.Name = "type_cb";
                constraint_cb.Width = 100;
                constraint_cb.Height = sp.Height - 5;
                constraint_cb.HorizontalAlignment = HorizontalAlignment.Left;
                constraint_cb.VerticalAlignment = VerticalAlignment.Top;
                constraint_cb.Margin = new Thickness(5, 5, 0, 0);
                string[] types = new string[] { "--select--", "Upper", "Lower", "Mean", "Exact" };
                foreach (string s in types) constraint_cb.Items.Add(s);
                constraint_cb.Text = defaultList[i].Item2;
                constraint_cb.HorizontalContentAlignment = HorizontalAlignment.Center;
                sp.Children.Add(constraint_cb);

                TextBox dose_tb = new TextBox();
                dose_tb.Name = "dose_tb";
                dose_tb.Width = 65;
                dose_tb.Height = sp.Height - 5;
                dose_tb.HorizontalAlignment = HorizontalAlignment.Left;
                dose_tb.VerticalAlignment = VerticalAlignment.Top;
                dose_tb.Margin = new Thickness(5, 5, 0, 0);
                dose_tb.Text = Convert.ToString(defaultList[i].Item4);
                dose_tb.TextAlignment = TextAlignment.Center;
                sp.Children.Add(dose_tb);

                TextBox vol_tb = new TextBox();
                vol_tb.Name = "vol_tb";
                vol_tb.Width = 70;
                vol_tb.Height = sp.Height - 5;
                vol_tb.HorizontalAlignment = HorizontalAlignment.Left;
                vol_tb.VerticalAlignment = VerticalAlignment.Top;
                vol_tb.Margin = new Thickness(5, 5, 0, 0);
                vol_tb.Text = Convert.ToString(defaultList[i].Item3);
                vol_tb.TextAlignment = TextAlignment.Center;
                sp.Children.Add(vol_tb);

                TextBox priority_tb = new TextBox();
                priority_tb.Name = "priority_tb";
                priority_tb.Width = 65;
                priority_tb.Height = sp.Height - 5;
                priority_tb.HorizontalAlignment = HorizontalAlignment.Left;
                priority_tb.VerticalAlignment = VerticalAlignment.Top;
                priority_tb.Margin = new Thickness(5, 5, 0, 0);
                priority_tb.Text = Convert.ToString(defaultList[i].Item5);
                priority_tb.TextAlignment = TextAlignment.Center;
                sp.Children.Add(priority_tb);

                Button clearOptStructBtn = new Button();
                clearOptBtnCounter++;
                clearOptStructBtn.Name = "clearOptStructBtn" + clearOptBtnCounter;
                clearOptStructBtn.Content = "Clear";
                clearOptStructBtn.Click += new RoutedEventHandler(this.clearOptStructBtn_click);
                clearOptStructBtn.Width = 60;
                clearOptStructBtn.Height = sp.Height - 5;
                clearOptStructBtn.HorizontalAlignment = HorizontalAlignment.Left;
                clearOptStructBtn.VerticalAlignment = VerticalAlignment.Top;
                clearOptStructBtn.Margin = new Thickness(10, 5, 0, 0);
                sp.Children.Add(clearOptStructBtn);

                opt_parameters.Children.Add(sp);
            }
        }

        private void loadNewConfigFile_Click(object sender, RoutedEventArgs e)
        {
            configFile = "";
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\configuration\\";
            openFileDialog.Filter = "ini files (*.ini)|*.ini|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog().Value) { if (!loadConfigurationSettings(openFileDialog.FileName)) { if (pi != null) displayConfigurationParameters(); } else MessageBox.Show("Error! Selected file is NOT valid!"); }
        }

        private bool loadConfigurationSettings(string file)
        {
            configFile = file;
            try
            {
                using (StreamReader reader = new StreamReader(configFile))
                {
                    string line;
                    List<Tuple<string, string, double, double, DoseValuePresentation>> planObjSclero_temp = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
                    List<Tuple<string, string, double, double, DoseValuePresentation>> planObjGeneral_temp = new List<Tuple<string, string, double, double, DoseValuePresentation>> { };
                    List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>> requestedTSstructures_temp = new List<Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>>> { };
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(line) && line.Substring(0, 1) != "%")
                        {
                            //start actually reading data when you find the begin executable configuration tab
                            if (line.Equals(":begin executable configuration:"))
                            {
                                while (!(line = reader.ReadLine()).Equals(":end executable configuration:"))
                                {
                                    if (!string.IsNullOrEmpty(line) && line.Substring(0, 1) != "%")
                                    {
                                        //useful info on this line
                                        if (line.Contains("="))
                                        {
                                            string parameter = line.Substring(0, line.IndexOf("="));
                                            string value = line.Substring(line.IndexOf("=") + 1, line.Length - line.IndexOf("=") - 1);
                                            if (double.TryParse(value, out double result))
                                            {
                                                if (parameter == "default number of optimizations") defautlNumOpt = value;
                                                else if (parameter == "default plan normalization") defaultPlanNorm = value;
                                                else if (parameter == "decision threshold") threshold = result;
                                                else if (parameter == "relative lower dose limit") lowDoseLimit = result;
                                            }
                                            else if (parameter == "documentation path")
                                            {
                                                documentationPath = value;
                                                if (documentationPath.LastIndexOf("\\") != documentationPath.Length - 1) documentationPath += "\\";
                                            }
                                            else if (parameter == "log file path")
                                            {
                                                logFilePath = value;
                                                if (logFilePath.LastIndexOf("\\") != logFilePath.Length - 1) logFilePath += "\\";
                                            }
                                            else if (parameter == "demo") { if (value != "") demo = bool.Parse(value); }
                                            else if (parameter == "check for spinning manny") { if (value != "") checkSpinningManny = bool.Parse(value); }
                                            else if (parameter == "run coverage check") { if (value != "") runCoverageCheckOption = bool.Parse(value); }
                                            else if (parameter == "run additional optimization") { if (value != "") runAdditionalOptOption = bool.Parse(value); }
                                            else if (parameter == "copy and save each plan") { if (value != "") copyAndSaveOption = bool.Parse(value); }
                                        }
                                        else if (line.Contains("add scleroderma plan objective")) planObjSclero_temp.Add(parsePlanObjective(line));
                                        else if (line.Contains("add plan objective")) planObjGeneral_temp.Add(parsePlanObjective(line));
                                        else if (line.Contains("add TS structure")) requestedTSstructures_temp.Add(parseTSstructure(line));
                                    }
                                }
                            }
                        }
                    }
                    if (planObjSclero_temp.Any()) planObjSclero = planObjSclero_temp;
                    if (planObjGeneral_temp.Any()) planObjGeneral = planObjGeneral_temp;
                    if (requestedTSstructures_temp.Any()) requestedTSstructures = requestedTSstructures_temp;
                }
                return false;
            }
            catch (Exception e) { MessageBox.Show(String.Format("Error could not load configuration file because: {0}\n\nAssuming default parameters", e.Message)); return true; }
        }

        private Tuple<string, string, double, double, DoseValuePresentation> parsePlanObjective(string line)
        {
            string structure = "";
            string constraintType = "";
            double doseVal = 0.0;
            double volumeVal = 0.0;
            DoseValuePresentation dvp;
            line = cropLine(line, "{");
            structure = line.Substring(0, line.IndexOf(","));
            line = cropLine(line, ",");
            constraintType = line.Substring(0, line.IndexOf(","));
            line = cropLine(line, ",");
            doseVal = double.Parse(line.Substring(0, line.IndexOf(",")));
            line = cropLine(line, ",");
            volumeVal = double.Parse(line.Substring(0, line.IndexOf(",")));
            line = cropLine(line, ",");
            if (line.Contains("Relative")) dvp = DoseValuePresentation.Relative;
            else dvp = DoseValuePresentation.Absolute;
            return Tuple.Create(structure, constraintType, doseVal, volumeVal, dvp);
        }

        private Tuple<string,double,double,double,int,List<Tuple<string,double,string,double>>> parseTSstructure(string line)
        {
            //type (Dmax or V), dose value for volume constraint (N/A for Dmax), equality or inequality, volume (%) or dose (%)
            List<Tuple<string, double, string, double>> constraints = new List<Tuple<string, double, string, double>> { };
            string structure = "";
            double lowDoseLevel = 0.0;
            double upperDoseLevel = 0.0;
            double volumeVal = 0.0;
            int priority = 0;
            try
            {
                line = cropLine(line, "{");
                structure = line.Substring(0, line.IndexOf(","));
                line = cropLine(line, ",");
                lowDoseLevel = double.Parse(line.Substring(0, line.IndexOf(",")));
                line = cropLine(line, ",");
                upperDoseLevel = double.Parse(line.Substring(0, line.IndexOf(",")));
                line = cropLine(line, ",");
                volumeVal = double.Parse(line.Substring(0, line.IndexOf(",")));
                line = cropLine(line, ",");
                priority = int.Parse(line.Substring(0, line.IndexOf(",")));
                line = cropLine(line, "{");

                while (!string.IsNullOrEmpty(line) && line.Substring(0, 1) != "}")
                {
                    string constraintType = "";
                    double doseVal = 0.0;
                    string inequality = "";
                    double queryVal = 0.0;
                    if (line.Substring(0, 1) == "f")
                    {
                        //only add for final optimization (i.e., one more optimization requested where current calculated dose is used as intermediate)
                        constraintType = "finalOpt";
                        if (!line.Contains(",")) line = cropLine(line, "}");
                        else line = cropLine(line, ",");
                    }
                    else
                    {
                        if (line.Substring(0, 1) == "V")
                        {
                            constraintType = "V";
                            line = cropLine(line, "V");
                            int index = 0;
                            while (line.ElementAt(index).ToString() != ">" && line.ElementAt(index).ToString() != "<") index++;
                            doseVal = double.Parse(line.Substring(0, index));
                            line = line.Substring(index, line.Length - index);
                        }
                        else
                        {
                            constraintType = "Dmax";
                            line = cropLine(line, "x");
                        }
                        inequality = line.Substring(0, 1);

                        if (!line.Contains(",")) { queryVal = double.Parse(line.Substring(1, line.IndexOf("}") - 1)); line = cropLine(line, "}"); }
                        else
                        {
                            queryVal = double.Parse(line.Substring(1, line.IndexOf(",") - 1));
                            line = cropLine(line, ",");
                        }
                    }
                    constraints.Add(Tuple.Create(constraintType, doseVal, inequality, queryVal));
                }

                return Tuple.Create(structure, lowDoseLevel, upperDoseLevel, volumeVal, priority, new List<Tuple<string, double, string, double>>(constraints));
            }
            catch (Exception e) { MessageBox.Show(String.Format("Error could not parse TS structure: {0}\nBecause: {1}", line, e.Message)); return Tuple.Create("", 0.0, 0.0, 0.0, 0, new List<Tuple<string, double, string, double>> { }); }
        }

        private string cropLine(string line, string cropChar) { return line.Substring(line.IndexOf(cropChar) + 1, line.Length - line.IndexOf(cropChar) - 1); }

        //same code as in the binary plug in
        private void clear_optParams_Click(object sender, RoutedEventArgs e) { clearAllOptimizationStructs(); }

        private void clearOptStructBtn_click(object sender, EventArgs e)
        {
            //same code as in binary plug in
            Button btn = (Button)sender;
            int i = 0;
            int k = 0;
            foreach (object obj in opt_parameters.Children)
            {
                foreach (object obj1 in ((StackPanel)obj).Children) if ((obj1.Equals(btn))) k = i;
                if (k > 0) break;
                i++;
            }

            //clear entire list if there are only two entries (header + 1 real entry)
            if (opt_parameters.Children.Count < 3) clearAllOptimizationStructs();
            else opt_parameters.Children.RemoveAt(k);
        }

        private void clearAllOptimizationStructs()
        {
            //same code as in binary plug in
            firstOptStruct = true;
            clearOptBtnCounter = 0;
            opt_parameters.Children.Clear();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (app == null) return;
            //be sure to close the patient before closing the application. Not doing so will result in unclosed timestamps in eclipse
            app.ClosePatient();
            app.Dispose();
        }
    }
}
