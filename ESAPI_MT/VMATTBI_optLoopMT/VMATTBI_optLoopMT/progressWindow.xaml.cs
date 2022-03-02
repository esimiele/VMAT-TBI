using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Runtime.InteropServices;

namespace VMATTBI_optLoop
{
    public partial class progressWindow : Window
    {
        //flags to let the code know if the user hit the 'Abort' button, if the optimization loop is finished, and if the GUI can close safely (you don't want to close it if the background thread hasn't stopped working)
        public bool abortOpt;
        public bool isFinished;
        public bool canClose;
        //flag that controls whether the code is run in 'Demo' mode. In demo mode, no coverage check, VMAT optimization, or dose calculations are performed. Instead, these statements have been replaced with Thread.Sleep(3000), which 
        //tells the code to 'sleep' for 3 seconds
        private bool demo;
        //total number of calculations/items that need to be completed during the optimization loop
        public int calcItems;
        //used to copy the instances of the background thread and the optimizationLoop class
        ESAPIworker slave;
        optimizationLoop op;
        //string to hold the patient MRN number
        string id = "";
        //path to where the log files should be written
        string logPath = "";

        //used for progress reporting
        string optPlanObjHeader = "";
        string optRequestTS = "";
        string optObjHeader = "";
        string optResHeader = "";
        //get instances of the stopwatch and dispatch timer to report how long the calculation takes at each reporting interval
        Stopwatch sw = new Stopwatch();
        DispatcherTimer dt = new DispatcherTimer();
        string currentTime = "";

        public progressWindow(ESAPIworker e, optimizationLoop o)
        {
            InitializeComponent();

            //flags to let the code know if the user wants to stop the optimization loop, is the optimization loop finished, and can the progress window close
            abortOpt = false;
            isFinished = false;
            canClose = false;
            //copy the thread instance and optimizationLoop class instance
            slave = e;
            op = o;
            //copy the patient MRN so the script will always write the output to my folder (so I don't have to worry about users forgetting to save the output)
            id = slave.data.id;

            //demo status
            demo = slave.data.isDemo;

            //copy log path
            logPath = slave.data.logFilePath;

            optPlanObjHeader = " Plan objectives:" + System.Environment.NewLine;
            optPlanObjHeader += " --------------------------------------------------------------------------" + System.Environment.NewLine;
            optPlanObjHeader += String.Format(" {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -9} |", "structure Id", "constraint type", "dose", "volume (%)", "dose type") + System.Environment.NewLine;
            optPlanObjHeader += " --------------------------------------------------------------------------" + System.Environment.NewLine;

            optRequestTS += String.Format("Requested tuning structures:") + System.Environment.NewLine;
            optRequestTS += " --------------------------------------------------------------------------" + System.Environment.NewLine;
            optRequestTS += String.Format(" {0, -15} | {1, -9} | {2, -10} | {3, -5} | {4, -8} | {5, -10} |", "structure Id", "low D (%)", "high D (%)", "V (%)", "priority", "constraint") + System.Environment.NewLine;
            optRequestTS += " --------------------------------------------------------------------------" + System.Environment.NewLine;
            
            //setup formating for progress window output text
            optObjHeader = " Updated optimization constraints:" + System.Environment.NewLine;
            optObjHeader += " -------------------------------------------------------------------------" + System.Environment.NewLine;
            optObjHeader += String.Format(" {0, -15} | {1, -16} | {2, -10} | {3, -10} | {4, -8} |" + System.Environment.NewLine, "structure Id", "constraint type", "dose (cGy)", "volume (%)", "priority");
            optObjHeader += " -------------------------------------------------------------------------" + System.Environment.NewLine;

            optResHeader = " Results of optimization:" + System.Environment.NewLine;
            optResHeader += " ---------------------------------------------------------------------------------------------------------" + System.Environment.NewLine;
            optResHeader += String.Format(" {0, -15} | {1, -16} | {2, -20} | {3, -16} | {4, -12} | {5, -9} |" + System.Environment.NewLine, "structure Id", "constraint type", "dose diff^2 (cGy^2)", "current priority", "cost", "cost (%)");
            optResHeader += " ---------------------------------------------------------------------------------------------------------" + System.Environment.NewLine;

            //set total number of milestones (used for calculation of percent progress completed)
            //7 milestones always have to be completed if coverage check is selected
            if (slave.data.oneMoreOpt) calcItems = (10 + 7 * slave.data.numOptimizations);
            else calcItems = (7 + 7 * slave.data.numOptimizations);
            //if coverage check is NOT selected, remove 4 of these milestones
            if (demo || !slave.data.runCoverageCheck) calcItems -= 5;
            if (slave.data.useFlash) calcItems += 2;

            //initialize and start the stopwatch
            runTime.Text = "00:00:00";
            dt.Tick += new EventHandler(dt_tick);
            dt.Interval = new TimeSpan(0, 0, 1);
            sw.Start();
            dt.Start();

            //start the optimization loop
            doStuff();
        }

        private void Abort_Click(object sender, RoutedEventArgs e)
        {
            //the user wants to stop the optimization loop. Set the abortOpt flag to true. The optimization loop will stop when it reaches an appropriate point
            if (!isFinished)
            {
                string message = System.Environment.NewLine + System.Environment.NewLine + 
                    " Abort command received!" + System.Environment.NewLine + " The optimization loop will be stopped at the next available stopping point!" + System.Environment.NewLine + " Be patient!";
                update.Text += message + System.Environment.NewLine;
                abortOpt = true;
                abortStatus.Text = "Canceling";
                abortStatus.Background = System.Windows.Media.Brushes.Yellow;
                updateLogFile(message);
            }
        }

        private void dt_tick(object sender, EventArgs e)
        {
            //increment the time on the progress window for each "tick", which is set to intervals of 1 second
            if (sw.IsRunning)
            {
                TimeSpan ts = sw.Elapsed;
                currentTime = String.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds);
                runTime.Text = currentTime;
            }
        }

        public void doStuff()
        {
            //execute the optimization loop on the daughter thread
            slave.DoWork(d =>
            {
                int percentCompletion = 0;
                //this dispatcher is for the daughter thread and tells the code to execute the below thread on the dauther thread asynchronously.
                //These commands in this class are used to update the progress window UI (under control of the daughter thread)
                Dispatcher.BeginInvoke((Action)(() => { abortStatus.Text = "Running"; }));
                //run preliminary checks on the plan before starting the optimization loop. The code for these checks is in the optimizationLoop class
                if (op.preliminaryChecks(d.plan))
                {
                    //preliminary checks have failed. Set the abort status and kill the optimization loop
                    Dispatcher.BeginInvoke((Action)(() => { abortStatus.Text = "Failed!"; }));
                    Dispatcher.BeginInvoke((Action)(() => { abortStatus.Background = System.Windows.Media.Brushes.Red; }));
                    sw.Stop();
                    dt.Stop();
                    canClose = true;
                    return;
                }
                //check if the user hit the 'Abort' button before/during the preliminary checks
                if (abortOpt)
                {
                    Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                    return;
                }

                string info = String.Format( " ---------------------------------------------------------------------------------------------------------" + System.Environment.NewLine +
                    " Date: {0}" + System.Environment.NewLine +
                    " Optimization parameters:" + System.Environment.NewLine +
                    " Run coverage check: {1}" + System.Environment.NewLine +
                    " Max number of optimizations: {2}" + System.Environment.NewLine +
                    " Run additional optimization to lower hotspots: {3}" + System.Environment.NewLine +
                    " Copy and save each optimized plan: {4}" + System.Environment.NewLine +
                    " Plan normalization: PTV V{5}cGy = {6:0.0}%" + System.Environment.NewLine,
                    DateTime.Now.ToString(), d.runCoverageCheck.ToString(), d.numOptimizations.ToString(), d.oneMoreOpt.ToString(), d.copyAndSavePlanItr.ToString(), d.plan.TotalDose.Dose.ToString(), d.targetVolCoverage.ToString());
                Dispatcher.BeginInvoke((Action)(() => { provideUpdate(info); }));

                if (d.useFlash)
                {
                    string message = String.Format(" I've found structures in the optimization list that have the keyword 'flash'!" + System.Environment.NewLine
                        + " I'm assuming you want to include flash in the optimization! Stop the loop if this is a mistake!" + System.Environment.NewLine);
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));
                }


                //structure, dvh data, current dose obj, dose diff^2, cost, current priority, priority difference
                string planObjectives = System.Environment.NewLine;
                planObjectives += optPlanObjHeader;
                foreach (Tuple<string,string,double,double,DoseValuePresentation> itr in d.planObj)
                {
                    //"structure Id", "constraint type", "dose (cGy or %)", "volume (%)", "Dose display (absolute or relative)"
                    planObjectives += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-9} |" + System.Environment.NewLine, itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);
                }
                Dispatcher.BeginInvoke((Action)(() => { provideUpdate(planObjectives); }));

                //print requested tuning structures
                string TSstructures = System.Environment.NewLine;
                TSstructures += optRequestTS;
                foreach (Tuple<string, double, double, double, int, List<Tuple<string, double, string, double>>> itr in d.requestedTSstructures)
                {
                    TSstructures += String.Format(" {0, -15} | {1, -9:N1} | {2,-10:N1} | {3,-5:N1} | {4,-8} |", itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);
                    if (!itr.Item6.Any()) TSstructures += String.Format(" {0,-10} |", "none") + System.Environment.NewLine;
                    else
                    {
                        int index = 0;
                        foreach (Tuple<string, double, string, double> itr1 in itr.Item6)
                        {
                            if (index == 0)
                            {
                                if (itr1.Item1.Contains("Dmax")) TSstructures += String.Format(" {0,-10} |", String.Format("{0}{1}{2}%", itr1.Item1, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                                else if (itr1.Item1.Contains("V")) TSstructures += String.Format(" {0,-10} |", String.Format("{0}{1}%{2}{3}%", itr1.Item1, itr1.Item2, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                                else TSstructures += String.Format(" {0,-10} |", String.Format("{0}", itr1.Item1)) + System.Environment.NewLine;
                            }
                            else
                            {
                                if (itr1.Item1.Contains("Dmax")) TSstructures += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}{2}%", itr1.Item1, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                                else if (itr1.Item1.Contains("V")) TSstructures += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}{1}%{2}{3}%", itr1.Item1, itr1.Item2, itr1.Item3, itr1.Item4)) + System.Environment.NewLine;
                                else TSstructures += String.Format(" {0,-59} | {1,-10} |", " ", String.Format("{0}", itr1.Item1)) + System.Environment.NewLine;
                            }
                            index++;
                        }
                    }
                }
                Dispatcher.BeginInvoke((Action)(() => { provideUpdate(TSstructures); }));

                //update the progress in the message window and the percent completion in the GUI
                Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Primilary checks passed"); }));

                //following the preliminary checks, perform a coverage check to ensure all portions of the ptv are covered by some amount of beam. Here, all optimization constraint priorities are zero-ed except for the ptv structures
                //if the global hotspot is > ~ 140%, then this indicates that a portion of the ptv is NOT covered by any of the beams and the process of normalizing to 90% coverage dramatically increases the hotspot.
                //arguments to runCoverageCheck: optimization parameters, instance of the plan, current percent completion, relative dose and target coverage needed for normalization
                if (demo || !d.runCoverageCheck) Dispatcher.BeginInvoke((Action)(() => { provideUpdate(" Skipping coverage check! Moving on to optimization loop!"); }));
                else if (!abortOpt)
                {
                    //this is NOT demo mode, the user has NOT requested to stop the optimization loop, and the user wants to run the coverage check.
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Running coverage Check!"); }));
                    if (runCoverageCheck(d.optParams, d.plan, percentCompletion, d.relativeDose, d.targetVolCoverage, d.useFlash))
                    {
                        //8/2/2020 changed messagebox to just an update as having a messagebox stops the progress of the optimization loop. 
                        string message = System.Environment.NewLine + String.Format(" I'm having trouble covering the target with the Rx Dose! Hot spot = {0:0.0}%", 100 * (d.plan.Dose.DoseMax3D.Dose / d.plan.TotalDose.Dose))
                            + Environment.NewLine + " Consider stopping the optimization and checking the beam arrangement!";
                        Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));
                    }
                    //increment percentCompletion variable by 4 to account for the runCoverage check progress. Normally I would pass by reference, but pass by reference isn't supported inside lambda expressions, so it needs to be done this way
                    percentCompletion += 4;
                }

                //not demo mode and coverage check completed. Or user asked to abort the optimization loop
                if (abortOpt)
                {
                    Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                    return;
                }

                //if the user set the number of optimizations to less than 1, just reset the optimization parameters to their original values and finish
                if (d.numOptimizations < 1)
                {
                    op.updateConstraints(d.optParams, d.plan);
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems)); setAbortStatus(); }));
                    return;
                }
                else
                {
                    if (!demo && d.runCoverageCheck) Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Coverage check completed! Commencing optimization loop!"); }));

                    //coverage check passed, now set some initial optimization parameters for each structure in the initial list
                    List<Tuple<string, string, double, double, int>> initialObj = new List<Tuple<string, string, double, double, int>> { };
                    int priority;
                    string message = System.Environment.NewLine;
                    message += optObjHeader;
                    foreach (Tuple<string, string, double, double, int> opt in d.optParams)
                    {
                        //leave the PTV priorities at their original values (i.e., 100)
                        if (opt.Item1.ToLower().Contains("ptv") || opt.Item1.ToLower().Contains("ts_jnx")) priority = opt.Item5;
                        //start OAR structure priorities at 2/3 of the values the user specified so there is some wiggle room for adjustment
                        else priority = (int)Math.Ceiling(((double)opt.Item5 * 2) / 3);
                        initialObj.Add(Tuple.Create(opt.Item1, opt.Item2, opt.Item3, opt.Item4, priority));
                        message += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-8} |" + System.Environment.NewLine, opt.Item1, opt.Item2, opt.Item3, opt.Item4, priority);
                    }
                    //reset the objectives and inform the user of the current optimization parameters
                    op.updateConstraints(initialObj, d.plan);
                    //update the current optimization parameters for this iteration
                    d.optParams = initialObj;
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));
                }
                if (demo) Thread.Sleep(3000);
                else d.app.SaveModifications();

                Dispatcher.BeginInvoke((Action)(() => { provideUpdate(" Starting optimization loop!"); }));
                //counter to keep track of how many optimization iterations have been performed
                int count = 0;
                while (count < d.numOptimizations)
                {
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), String.Format(" Iteration {0}:", count + 1)); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //optimize with intermediate dose (AAA algorithm).
                        d.plan.OptimizeVMAT(new OptimizationOptionsVMAT(OptimizationIntermediateDoseOption.NoIntermediateDose, ""));
                        d.app.SaveModifications();
                    }

                    //check if user wants to stop
                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Optimization finished! Calculating intermediate dose!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //calculate dose
                        d.plan.CalculateDose();
                        d.app.SaveModifications();
                    }

                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Dose calculated! Continuing optimization!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //continue optimization using existing dose as intermediate (AAA algorithm).
                        d.plan.OptimizeVMAT(new OptimizationOptionsVMAT(OptimizationOption.ContinueOptimizationWithPlanDoseAsIntermediateDose, ""));
                        d.app.SaveModifications();
                    }

                    //check if user wants to stop
                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Optimization finished! Calculating dose!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //calculate dose
                        d.plan.CalculateDose();
                        d.app.SaveModifications();
                    }

                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Dose calculated, normalizing plan!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    //normalize
                    op.normalizePlan(d.plan, d.relativeDose, d.targetVolCoverage, d.useFlash);
                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Plan normalized! Evaluating plan quality and updating constraints!"); }));
                    //evaluate the new plan for quality and make any adjustments to the optimization parameters
                    optimizationLoop.evalStruct e = op.evaluateAndUpdatePlan(d.plan, d.optParams, d.planObj, d.requestedTSstructures, d.threshold, d.lowDoseLimit, (d.oneMoreOpt && ((count + 1) == d.numOptimizations)));

                    //updated optimization constraint list is empty, which means that all plan objectives have been met. Let the user know and break the loop. Also set oneMoreOpt to false so that extra optimization is not performed
                    if (!e.updatedObj.Any())
                    {
                        Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" All plan objectives have been met! Exiting!")); }));
                        d.oneMoreOpt = false;
                        break;
                    }

                    //did the user request to copy and save each plan iteration from the optimization loop?
                    if (!demo && d.copyAndSavePlanItr)
                    {
                        //not demo mode and user requested to save each plan iteration
                        if (d.oneMoreOpt || ((count + 1) != d.numOptimizations))
                        {
                            //either user requested one more optimization (always copy and save) or this is not the last loop iteration (used in the case where the user elected NOT to do one more optimization
                            //but still wants to copy and save each plan). We don't want to copy and save the plan on the last loop iteration when oneMoreOpt is false because we will end up with two copies of
                            //the same plan!
                            Course c = d.plan.Course;
                            //this copies the plan and the dose!
                            ExternalPlanSetup newPlan = (ExternalPlanSetup)c.CopyPlanSetup(d.plan);
                            newPlan.Id = String.Format("opt itr {0}", count + 1);
                        }
                    }

                    //print the results of the quality check for this optimization
                    string optResults = System.Environment.NewLine;
                    optResults += optResHeader;
                    int index = 0;
                    //structure, dvh data, current dose obj, dose diff^2, cost, current priority, priority difference
                    foreach (Tuple<Structure, DVHData, double, double, double, int> itr in e.diffPlanOpt)
                    {
                        string id = "";
                        //grab the structure id from the optParams list (better to work with string literals rather than trying to access the structure id through the structure object instance in the diffPlanOpt data structure)
                        id = d.optParams.ElementAt(index).Item1;
                        //"structure Id", "constraint type", "dose diff^2 (cGy^2)", "current priority", "cost", "cost (%)"
                        optResults += String.Format(" {0, -15} | {1, -16} | {2, -20:N1} | {3, -16} | {4, -12:N1} | {5, -9:N1} |" + System.Environment.NewLine, id, d.optParams.ElementAt(index).Item2, itr.Item4, itr.Item6, itr.Item5, 100 * itr.Item5 / e.totalCostPlanOpt);
                        index++;
                    }
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(optResults); }));

                    //print useful info about target coverage and global dmax
                    Structure target;
                    if (d.useFlash) target = e.diffPlanOpt.First(x => x.Item1.Id.ToLower() == "ts_ptv_flash").Item1;
                    else target = e.diffPlanOpt.First(x => x.Item1.Id.ToLower() == "ts_ptv_vmat").Item1;
                    string message = " Additional plan infomation: " + System.Environment.NewLine +
                                     String.Format(" Plan global Dmax = {0:0.0}%", 100 * (d.plan.Dose.DoseMax3D.Dose / d.plan.TotalDose.Dose)) + System.Environment.NewLine +
                                     String.Format(" {0} Dmax = {1:0.0}%", target.Id, d.plan.GetDoseAtVolume(target, 0.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                     String.Format(" {0} Dmin = {1:0.0}%", target.Id, d.plan.GetDoseAtVolume(target, 100.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                     String.Format(" {0} V90% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(90.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                     String.Format(" {0} V110% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(110.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                     String.Format(" {0} V120% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(120.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));

                    //really crank up the priority and lower the dose objective on the cooler on the last iteration of the optimization loop
                    //this is basically here to avoid having to call op.updateConstraints a second time (if this batch of code was placed outside of the loop)
                    if (d.oneMoreOpt && ((count + 1) == d.numOptimizations))
                    {
                        //go through the current list of optimization objects and add all of them to finalObj vector. ADD COMMENTS!
                        List<Tuple<string, string, double, double, int>> finalObj = new List<Tuple<string, string, double, double, int>> { };
                        foreach (Tuple<string, string, double, double, int> itr in e.updatedObj)
                        {
                            //get maximum priority and assign it to the cooler structure to really push the hotspot down. Also lower dose objective
                            if (itr.Item1.ToLower().Contains("ts_cooler"))
                            {
                                finalObj.Add(new Tuple<string, string, double, double, int>(itr.Item1, itr.Item2, 0.98*itr.Item3, itr.Item4, Math.Max(itr.Item5, (int)(0.9*(double)e.updatedObj.Max(x => x.Item5)))));
                            }
                            else finalObj.Add(itr);
                        }
                        //set e.updatedObj to be equal to finalObj
                        e.updatedObj = finalObj;
                    }

                    //print the updated optimization objectives to the user
                    string newObj = System.Environment.NewLine;
                    newObj += optObjHeader;
                    foreach (Tuple<string, string, double, double, int> itr in e.updatedObj)
                        newObj += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-8} |" + System.Environment.NewLine, itr.Item1, itr.Item2, itr.Item3, itr.Item4, itr.Item5);

                    //update the optimization constraints in the plan
                    op.updateConstraints(e.updatedObj, d.plan);
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), newObj); }));
                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }
                    //increment the counter, update d.optParams so it is set to the initial optimization constraints at the BEGINNING of the optimization iteration, and save the changes to the plan
                    count++;
                    d.optParams = e.updatedObj;
                    if(!demo) d.app.SaveModifications();
                }

                //option to run one additional optimization (can be requested on the main GUI)
                if (d.oneMoreOpt)
                {
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Running one final optimization starting at MR3 to try and reduce global plan hotspots!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    //one final push to lower the global plan hotspot if the user asked for it
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //run optimization using current dose as intermediate dose. This will start the optimization at MR3 or MR4 (depending on the configuration of Eclipse)
                        d.plan.OptimizeVMAT(new OptimizationOptionsVMAT(OptimizationOption.ContinueOptimizationWithPlanDoseAsIntermediateDose, ""));
                        d.app.SaveModifications();
                    }

                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Optimization finished! Calculating dose!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                    if (demo) Thread.Sleep(3000);
                    else
                    {
                        //calculate dose
                        d.plan.CalculateDose();
                        d.app.SaveModifications();
                    }

                    if (abortOpt)
                    {
                        Dispatcher.BeginInvoke((Action)(() => { setAbortStatus(); }));
                        return;
                    }

                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Dose calculated, normalizing plan!"); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}" + System.Environment.NewLine, currentTime)); }));
                    //normalize
                    op.normalizePlan(d.plan, d.relativeDose, d.targetVolCoverage, d.useFlash);

                    //print useful info about target coverage and global dmax
                    Structure target;
                    if (d.useFlash) target = d.plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_flash");
                    else target = d.plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_vmat");
                    string message = " Final plan infomation: " + System.Environment.NewLine +
                                    String.Format(" Plan global Dmax = {0:0.0}%", 100 * (d.plan.Dose.DoseMax3D.Dose / d.plan.TotalDose.Dose)) + System.Environment.NewLine +
                                    String.Format(" {0} Dmax = {1:0.0}%", target.Id, d.plan.GetDoseAtVolume(target, 0.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                    String.Format(" {0} Dmin = {1:0.0}%", target.Id, d.plan.GetDoseAtVolume(target, 100.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                    String.Format(" {0} V90% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(90.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                    String.Format(" {0} V110% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(110.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                    String.Format(" {0} V120% = {1:0.0}%", target.Id, d.plan.GetVolumeAtDose(target, new DoseValue(120.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));
                }

                if (d.useFlash)
                {
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), String.Format(System.Environment.NewLine + " Removing flash, recalculating dose, and renormalizing to TS_PTV_VMAT!")); }));
                    Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));

                    Structure bolus = d.plan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "bolus_flash");
                    if (bolus == null)
                    {
                        //no structure named bolus_flash found. This is a problem. 
                        Dispatcher.BeginInvoke((Action)(() => { provideUpdate(" No structure named 'BOLUS_FLASH' found in structure set! Exiting!"); }));
                    }
                    else
                    {
                        //reset dose calculation matrix for each plan in the current course. Sorry! You will have to recalculate dose to EVERY plan!
                        string calcModel = d.plan.GetCalculationModel(CalculationType.PhotonVolumeDose);
                        List<ExternalPlanSetup> plansWithCalcDose = new List<ExternalPlanSetup> { };
                        foreach (ExternalPlanSetup p in d.plan.Course.ExternalPlanSetups)
                        {
                            if (p.IsDoseValid && p.StructureSet == d.plan.StructureSet)
                            {
                                p.ClearCalculationModel(CalculationType.PhotonVolumeDose);
                                p.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
                                plansWithCalcDose.Add(p);
                            }
                        }
                        //reset the bolus dose to undefined
                        bolus.ResetAssignedHU();
                        //recalculate dose to all the plans that had previously had dose calculated in the current course
                        if (demo) Thread.Sleep(3000);
                        else
                        {
                            foreach (ExternalPlanSetup p in plansWithCalcDose) p.CalculateDose();

                        }
                        Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Dose calculated, normalizing plan!"); }));
                        Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));
                        //"trick" the normalizePlan method into thinking we are not using flash. Therefore, it will normalize to TS_PTV_VMAT instead of TS_PTV_FLASH (i.e., set useFlash to false)
                        op.normalizePlan(d.plan, d.relativeDose, d.targetVolCoverage, false);
                    }
                }

                //optimization loop is finished, let user know, and save the changes to the plan
                isFinished = true;
                Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), System.Environment.NewLine + " Finished!"); setAbortStatus(); }));
                if(!demo) d.app.SaveModifications();
            });
        }

        public bool runCoverageCheck(List<Tuple<string, string, double, double, int>> optParams, ExternalPlanSetup plan, int percentCompletion, double relativeDose, double targetVolCoverage, bool useFlash)
        {
            //zero all optimization objectives except those in the target
            List<Tuple<string, string, double, double, int>> targetOnlyObj = new List<Tuple<string, string, double, double, int>> { };
            int priority;
            string message = optObjHeader;

            foreach (Tuple<string, string, double, double, int> opt in optParams)
            {
                if (opt.Item1.ToLower().Contains("ptv") || opt.Item1.ToLower().Contains("ts_jnx")) priority = opt.Item5;
                else priority = 0;
                targetOnlyObj.Add(Tuple.Create(opt.Item1, opt.Item2, opt.Item3, opt.Item4, priority));
                //record the optimization constraints for each structure after zero-ing the priorities. This information will be reported to the user in a progress update
                message += String.Format(" {0, -15} | {1, -16} | {2,-10:N1} | {3,-10:N1} | {4,-8} |" + System.Environment.NewLine, opt.Item1, opt.Item2, opt.Item3, opt.Item4, priority);
            }
            //update the constraints and provide an update to the user
            op.updateConstraints(targetOnlyObj, plan);
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), message); }));

            //run one optimization with NO intermediate dose.
            plan.OptimizeVMAT(new OptimizationOptionsVMAT(OptimizationIntermediateDoseOption.NoIntermediateDose, ""));
            if (abortOpt) return false;
            //provide update
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Optimization finished on coverage check! Calculating dose!"); }));
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Elapsed time: {0}", currentTime)); }));

            //calculate dose (using AAA algorithm)
            plan.CalculateDose();
            if (abortOpt) return false;
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Dose calculated for coverage check, normalizing plan!"); }));

            //normalize plan
            op.normalizePlan(plan, relativeDose, targetVolCoverage, useFlash);
            if (abortOpt) return false;
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate((int)(100 * (++percentCompletion) / calcItems), " Plan normalized!"); }));

            //print useful info about target coverage and global dmax
            Structure target;
            if (useFlash) target = plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_flash");
            else target = plan.StructureSet.Structures.First(x => x.Id.ToLower() == "ts_ptv_vmat");
            message = " Additional plan infomation: " + System.Environment.NewLine +
                                String.Format(" Plan global Dmax = {0:0.0}%", 100 * (plan.Dose.DoseMax3D.Dose / plan.TotalDose.Dose)) + System.Environment.NewLine +
                                String.Format(" {0} Dmax = {1:0.0}%", target.Id, plan.GetDoseAtVolume(target, 0.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                String.Format(" {0} Dmin = {1:0.0}%", target.Id, plan.GetDoseAtVolume(target, 100.0, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose) + System.Environment.NewLine +
                                String.Format(" {0} V90% = {1:0.0}%", target.Id, plan.GetVolumeAtDose(target, new DoseValue(90.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                String.Format(" {0} V110% = {1:0.0}%", target.Id, plan.GetVolumeAtDose(target, new DoseValue(110.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative)) + System.Environment.NewLine +
                                String.Format(" {0} V120% = {1:0.0}%", target.Id, plan.GetVolumeAtDose(target, new DoseValue(120.0, DoseValue.DoseUnit.Percent), VolumePresentation.Relative));
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate(message); }));

            //calculate global Dmax expressed as a percent of the prescription dose (if dose has been calculated)
            if (plan.IsDoseValid && ((plan.Dose.DoseMax3D.Dose / plan.TotalDose.Dose) > 1.40)) return true;
            //else MessageBox.Show(String.Format("max dose: {0}%", 100 * (plan.Dose.DoseMax3D.Dose / plan.TotalDose.Dose)));
            return false;
        }

        //three overloaded methods to provide periodic updates on the progress of the optimization loop
        public void provideUpdate(int percentComplete, string message)
        {
            progress.Value = percentComplete;
            update.Text += message + System.Environment.NewLine;
            scroller.ScrollToBottom();
            updateLogFile(message);
        }

        public void provideUpdate(int percentComplete) { progress.Value = percentComplete; }

        public void provideUpdate(string message) { update.Text += message + System.Environment.NewLine; scroller.ScrollToBottom(); updateLogFile(message); }

        private void updateLogFile(string output)
        {
            //this is here to check if the directory and file already exist. An alternative method would be to create a streamwriter in the constructor of this class, but because this program runs for several hours and I have no
            //control over the shared drive, there may be a situation where the streamwriter is created and wants to write to the file after a few hours and (for whatever reason) the directory/file is gone. In this case, it would likely
            //crash the program
            if (Directory.Exists(logPath))
            {
                output += System.Environment.NewLine;
                string fileName = logPath + id + ".txt";
                File.AppendAllText(fileName, output);
            }
        }

        //option to write the results to a text file in a user-specified location. A window will pop-up asking the user to navigate to their chosen directory and save the file with a custom name
        private void WriteResults_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Title = "Choose text file output",
                CheckPathExists = true,

                DefaultExt = "txt",
                Filter = "txt files (*.txt)|*.txt",
                FilterIndex = 2,
                RestoreDirectory = true,
            };

            if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string output = update.Text;
                string fileName = saveFileDialog1.FileName;
                File.WriteAllText(fileName, output);
                update.Text += String.Format(System.Environment.NewLine + " Output written to text file at: {0}" + System.Environment.NewLine, string.Concat(fileName));
            }
        }

        private void setAbortStatus()
        {
            if (abortOpt)
            {
                //the user requested to abort the optimization loop
                abortStatus.Text = "Aborted!";
                abortStatus.Background = System.Windows.Media.Brushes.Red;
            }
            else
            {
                //the optimization loop finished successfully
                abortStatus.Text = "Finished!";
                abortStatus.Background = System.Windows.Media.Brushes.LimeGreen;
            }
            //stop the clock and report the total run time. Also set the canClose flag to true to let the code know the background thread has finished working and it is safe to close
            sw.Stop();
            dt.Stop();
            Dispatcher.BeginInvoke((Action)(() => { provideUpdate(String.Format(" Total run time: {0}", currentTime)); }));

            canClose = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //extremely annoying message letting the user know that they cannot shut down the program until the optimization loop reaches a safe stopping point. The confirm window will keep popping up until 
            //the optimization loop reaches a safe stopping point. At that time, the user can close the application. If the user closes the progress window before that time, the background thread will still be working.
            //If the user forces the application to close, the timestamp within eclipse will still be there and it is not good to kill multithreaded applications in this way.
            //Basically, this code is an e-bomb, and will ensure the program can't be killed by the user until a safe stopping point has been reached (at least without the user of the task manager)
            while (!canClose)
            {
                if (!abortOpt)
                {
                    abortStatus.Text = "Canceling";
                    abortStatus.Background = System.Windows.Media.Brushes.Yellow;
                    abortOpt = true;
                }
                confirmUI CUI = new VMATTBI_optLoop.confirmUI();
                CUI.message.Text = String.Format("I can't close until the optimization loop has stopped!"
                    + Environment.NewLine + "Please wait until the abort status says 'Aborted' or 'Finished' and then click 'Confirm'.");
                CUI.ShowDialog();
            }
        }
    }
}
