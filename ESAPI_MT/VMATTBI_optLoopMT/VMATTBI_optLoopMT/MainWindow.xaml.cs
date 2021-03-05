using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VMS.TPS.Common.Model.Types;
using VMS.TPS.Common.Model.API;
using System.Reflection;


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
        //point this to the directory holding the documentation files
        string documentationPath = @"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\Research\VMAT_TBI\documentation\";
        //default number of optimizations to perform
        string defautlNumOpt = "3";
        //default plan normalization (i.e., PTV100% = 90%) 
        string defaultPlanNorm = "90";
        //MLC model
        string MLCmodel = "1763";

/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        VMS.TPS.Common.Model.API.Application app = VMS.TPS.Common.Model.API.Application.CreateApplication();
        Patient pi = null;
        bool firstOptStruct = true;
        int clearOptBtnCounter = 0;
        bool scleroTrial = false;
        bool runCoverageCheck = false;
        bool runOneMoreOpt = false;
        bool copyAndSavePlanItr = false;
        bool useFlash = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "VMAT_TBI_guide.pdf");
        }

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(documentationPath + "TBI_executable_quickStart_guide.pdf");
        }

        private void targetNormInfo_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("This is used to set the plan normalization. What percentage of the PTV volume should recieve the prescription dose?");
        }

        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            //open the patient with the user-entered MRN number
            string pat_mrn = MRN.Text;
            clearEverything();
            try
            {
                app.ClosePatient();
                pi = app.OpenPatientById(pat_mrn);
                //grab instances of the course and VMAT tbi plans that were created using the binary plug in script. This is explicitly here to let the user know if there is a problem with the course OR plan
                Course c = pi.Courses.FirstOrDefault(x => x.Id.ToLower() == "vmat tbi");
                if (c == null)
                {
                    MessageBox.Show("No course named VMAT TBI!");
                    return;
                }

                ExternalPlanSetup plan = c.ExternalPlanSetups.FirstOrDefault(x => x.Id.ToLower() == "_vmat tbi");
                if (plan == null)
                {
                    MessageBox.Show("No plan named _VMAT TBI!");
                    return;
                }

                //populate the optimization stackpanel with the optimization parameters that were stored in the VMAT TBI plan
                populateOptimizationTab(plan);
                //populate the prescription text boxes with the prescription stored in the VMAT TBI plan
                populateRx(plan);
                //set the default parameters for the optimization loop
                runCoverageCk.IsChecked = false;
                numOptLoops.Text = defautlNumOpt;
                runAdditionalOpt.IsChecked = true;
                copyAndSave.IsChecked = false;
                targetNormTB.Text = defaultPlanNorm;
            }
            catch
            { MessageBox.Show("No such patient exists!"); }
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
            ExternalPlanSetup plan = getPlan();
            if (plan == null) return;
            else populateOptimizationTab(plan);
        }

        private void startOpt_Click(object sender, RoutedEventArgs e)
        {
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
            ExternalPlanSetup plan = getPlan();
            if (plan == null) return;

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

            //start the optimization loop (all saving to the database is performed in the progressWindow class)
            pi.BeginModifications();
            optimizationLoop optLoop = new optimizationLoop(plan, optParametersList, planNorm, numOptimizations, scleroTrial, runCoverageCheck, runOneMoreOpt, copyAndSavePlanItr, useFlash, MLCmodel, app);
        }

        private void add_constraint_Click(object sender, RoutedEventArgs e)
        {
            //add a blank contraint to the list
            ExternalPlanSetup plan = getPlan();
            if (plan != null)
            {
                add_opt_volumes(plan.StructureSet, new List<Tuple<string, string, double, double, int>> { Tuple.Create("--select--", "--select--", 0.0, 0.0, 0) });
                optParamScroller.ScrollToBottom();
            }
        }

        private ExternalPlanSetup getPlan()
        {
            //grab an instance of the VMAT TBI plan. Return null if it isn't found
            if (pi == null) return null;
            Course c = pi.Courses.FirstOrDefault(x => x.Id.ToLower() == "vmat tbi");
            if (c == null) return null;

            ExternalPlanSetup plan = c.ExternalPlanSetups.FirstOrDefault(x => x.Id.ToLower() == "_vmat tbi");
            if (plan == null) return null;
            return plan;
        }

        private void add_opt_header()
        {
            //same code from the binary plug in
            StackPanel sp1 = new StackPanel();
            sp1.Height = 30;
            sp1.Width = opt_parameters.Width;
            sp1.Orientation = Orientation.Horizontal;
            sp1.Margin = new Thickness(5, 0, 5, 5);

            Label strName = new Label();
            strName.Content = "Structure";
            strName.HorizontalAlignment = HorizontalAlignment.Center;
            strName.VerticalAlignment = VerticalAlignment.Top;
            strName.Width = 110;
            strName.FontSize = 14;
            strName.Margin = new Thickness(27, 0, 0, 0);

            Label spareType = new Label();
            spareType.Content = "Constraint";
            spareType.HorizontalAlignment = HorizontalAlignment.Center;
            spareType.VerticalAlignment = VerticalAlignment.Top;
            spareType.Width = 90;
            spareType.FontSize = 14;
            spareType.Margin = new Thickness(2, 0, 0, 0);

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
                opt_str_cb.Width = 120;
                opt_str_cb.Height = sp.Height - 5;
                opt_str_cb.HorizontalAlignment = HorizontalAlignment.Left;
                opt_str_cb.VerticalAlignment = VerticalAlignment.Top;
                opt_str_cb.Margin = new Thickness(5, 5, 0, 0);

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
                clearOptStructBtn.Width = 50;
                clearOptStructBtn.Height = sp.Height - 5;
                clearOptStructBtn.HorizontalAlignment = HorizontalAlignment.Left;
                clearOptStructBtn.VerticalAlignment = VerticalAlignment.Top;
                clearOptStructBtn.Margin = new Thickness(10, 5, 0, 0);
                sp.Children.Add(clearOptStructBtn);

                opt_parameters.Children.Add(sp);
            }
        }

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
            //be sure to close the patient before closing the application. Not doing so will result in unclosed timestamps in eclipse
            app.ClosePatient();
            app.Dispose();
        }
    }
}
