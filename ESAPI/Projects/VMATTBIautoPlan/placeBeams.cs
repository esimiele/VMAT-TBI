using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Runtime.InteropServices;
using System.Windows.Media.Media3D;

namespace VMATTBIautoPlan
{
    class placeBeams
    {
        int numIsos;
        int numVMATIsos;
        int[] numBeams;
        public List<string> isoNames;
        bool checkIsoPlacement = false;
        double checkIsoPlacementLimit = 5.0;
        double isoSeparation = 0;
        Patient pi;
        StructureSet selectedSS;
        Structure target = null;
        Tuple<int, DoseValue> prescription;
        Course tbi;
        public ExternalPlanSetup plan = null;
        ExternalPlanSetup legs_planUpper = null;
        bool singleAPPAplan;

        //5-5-2020 ask nataliya about importance of matching collimator angles to CW and CCW rotations...
        double[] collRot;
        double[] CW = { 181.0, 179.0 };
        double[] CCW = {179.0, 181.0 };
        ExternalBeamMachineParameters ebmpArc;
        ExternalBeamMachineParameters ebmpStatic;
        List<VRect<double>> jawPos;
        private string calculationModel = "";
        private string optimizationModel = "";
        private string useGPUdose = "";
        private string useGPUoptimization = "";
        private string MRrestart = "";
        private bool useFlash;
        private bool contourOverlap = false;
        private double contourOverlapMargin;
        public List<Structure> jnxs = new List<Structure> { };

        public placeBeams(StructureSet ss, Tuple<int, DoseValue> presc, List<string> i, int iso, int vmatIso, bool appaPlan, int[] beams, double[] coll, List<VRect<double>> jp, string linac, string energy, string calcModel, string optModel, string gpuDose, string gpuOpt, string mr, bool flash)
        {
            selectedSS = ss;
            pi = selectedSS.Patient;
            prescription = presc;
            isoNames = new List<string>(i);
            numIsos = iso;
            numVMATIsos = vmatIso;
            singleAPPAplan = appaPlan;
            numBeams = beams;
            collRot = coll;
            jawPos = new List<VRect<double>>(jp);
            ebmpArc = new ExternalBeamMachineParameters(linac, energy, 600, "ARC", null);
            //AP/PA beams always use 6X
            ebmpStatic = new ExternalBeamMachineParameters(linac, "6X", 600, "STATIC", null);
            //copy the calculation model
            calculationModel = calcModel;
            optimizationModel = optModel;
            useGPUdose = gpuDose;
            useGPUoptimization = gpuOpt;
            MRrestart = mr;
            useFlash = flash;
        }

        public placeBeams(StructureSet ss, Tuple<int, DoseValue> presc, List<string> i, int iso, int vmatIso, bool appaPlan, int[] beams, double[] coll, List<VRect<double>> jp, string linac, string energy, string calcModel, string optModel, string gpuDose, string gpuOpt, string mr, bool flash, double overlapMargin)
        {
            selectedSS = ss;
            pi = selectedSS.Patient;
            prescription = presc;
            isoNames = new List<string>(i);
            numIsos = iso;
            numVMATIsos = vmatIso;
            singleAPPAplan = appaPlan;
            numBeams = beams;
            collRot = coll;
            jawPos = new List<VRect<double>>(jp);
            ebmpArc = new ExternalBeamMachineParameters(linac, energy, 600, "ARC", null);
            //AP/PA beams always use 6X
            ebmpStatic = new ExternalBeamMachineParameters(linac, "6X", 600, "STATIC", null);
            //copy the calculation model
            calculationModel = calcModel;
            optimizationModel = optModel;
            useGPUdose = gpuDose;
            useGPUoptimization = gpuOpt;
            MRrestart = mr;
            useFlash = flash;
            //user wants to contour the overlap between fields in adjacent VMAT isocenters
            contourOverlap = true;
            contourOverlapMargin = overlapMargin;
        }

        public ExternalPlanSetup generate_beams()
        {
            if (createPlan()) return null;
            List<VVector> isoLocations = getIsocenterPositions();
            if (contourOverlap) contourFieldOverlap(isoLocations);
            set_beams(isoLocations);

            if (checkIsoPlacement) MessageBox.Show(String.Format("WARNING: < {0:0.00} cm margin at most superior and inferior locations of body! Verify isocenter placement!", checkIsoPlacementLimit/10));
            return plan;
        }

        private bool createPlan()
        {
            //look for a course name VMAT TBI. If it does not exit, create it, otherwise load it into memory
            if (!selectedSS.Patient.Courses.Where(x => x.Id == "VMAT TBI").Any())
            {
                if (selectedSS.Patient.CanAddCourse())
                {
                    tbi = selectedSS.Patient.AddCourse();
                    tbi.Id = "VMAT TBI";
                }
                else
                {
                    MessageBox.Show("Error! \nCan't add a treatment course to the patient!");
                    return true;
                }
            }
            else tbi = selectedSS.Patient.Courses.FirstOrDefault(x => x.Id == "VMAT TBI");

            //6-10-2020 EAS, research system only!
            //if (tbi.ExternalPlanSetups.Where(x => x.Id == "_VMAT TBI").Any()) if (tbi.CanRemovePlanSetup((tbi.ExternalPlanSetups.First(x => x.Id == "_VMAT TBI")))) tbi.RemovePlanSetup(tbi.ExternalPlanSetups.First(x => x.Id == "_VMAT TBI"));
            if (tbi.ExternalPlanSetups.Where(x => x.Id == "_VMAT TBI").Any())
            {
                MessageBox.Show("A plan named '_VMAT TBI' Already exists! \nESAPI can't remove plans in the clinical environment! \nPlease manually remove this plan and try again.");
                return true;
            }
            plan = tbi.AddExternalPlanSetup(selectedSS);
            //100% dose prescribed in plan and plan ID is _VMAT TBI
            plan.SetPrescription(prescription.Item1, prescription.Item2, 1.0);
            plan.Id = "_VMAT TBI";
            //ask the user to set the calculation model if not calculation model was set in UI.xaml.cs (up near the top with the global parameters)
            if(calculationModel == "")
            {
                IEnumerable<string> models = plan.GetModelsForCalculationType(CalculationType.PhotonVolumeDose);
                selectItem SUI = new VMATTBIautoPlan.selectItem();
                SUI.title.Text = "No calculation model set!" + Environment.NewLine + "Please select a calculation model!";
                foreach (string s in plan.GetModelsForCalculationType(CalculationType.PhotonVolumeDose)) SUI.itemCombo.Items.Add(s);
                SUI.ShowDialog();
                if (!SUI.confirm) return true;
                //get the plan the user chose from the combobox
                calculationModel = SUI.itemCombo.SelectedItem.ToString();

                //just an FYI that the calculation will likely run out of memory and crash the optimization when Acuros is used
                if(calculationModel.ToLower().Contains("acuros") || calculationModel.ToLower().Contains("axb"))
                {
                    confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                    CUI.message.Text = "Warning!" + Environment.NewLine + "The optimization will likely crash (i.e., run out of memory) if Acuros is used!" + Environment.NewLine + "Continue?!";
                    CUI.ShowDialog();
                    if (!CUI.confirm) return true;
                }
            }
            plan.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);
            plan.SetCalculationModel(CalculationType.PhotonVMATOptimization, optimizationModel);

            //Dictionary<string, string> d = plan.GetCalculationOptions(plan.GetCalculationModel(CalculationType.PhotonVMATOptimization));
            //string m = "";
            //foreach (KeyValuePair<string, string> t in d) m += String.Format("{0}, {1}", t.Key, t.Value) + System.Environment.NewLine;
            //MessageBox.Show(m);

            //set the GPU dose calculation option (only valid for acuros)
            if (useGPUdose == "Yes" && !calculationModel.Contains("AAA")) plan.SetCalculationOption(calculationModel, "UseGPU", useGPUdose);
            else plan.SetCalculationOption(calculationModel, "UseGPU", "No");

            //set MR restart level option for the photon optimization
            plan.SetCalculationOption(optimizationModel, "VMAT/MRLevelAtRestart", MRrestart);

            //set the GPU optimization option
            if (useGPUoptimization == "Yes") plan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", useGPUoptimization);
            else plan.SetCalculationOption(optimizationModel, "General/OptimizerSettings/UseGPU", "No");

            //reference point can only be added for a plan that IS CURRENTLY OPEN
            //plan.AddReferencePoint(selectedSS.Structures.First(x => x.Id == "TS_PTV_VMAT"), null, "VMAT TBI", "VMAT TBI");

            //6-10-2020 EAS, research system only!
            if ((numIsos > numVMATIsos) && tbi.ExternalPlanSetups.Where(x => x.Id.ToLower().Contains("legs")).Any())
            {
                MessageBox.Show("Plan(s) with the string 'legs' already exists! \nESAPI can't remove plans in the clinical environment! \nPlease manually remove this plan and try again.");
                return true;
            }

            //these need to be fixed
            //v16 of Eclipse allows for the creation of a plan with a named target structure and named primary reference point. Neither of these options are available in v15
            //plan.TargetVolumeID = selectedSS.Structures.First(x => x.Id == "TS_PTV_VMAT");
            //plan.PrimaryReferencePoint = plan.ReferencePoints.Fisrt(x => x.Id == "VMAT TBI");

            return false;
        }

        private List<VVector> getIsocenterPositions()
        {
            List<VVector> iso = new List<VVector> { };
            Image image = selectedSS.Image;
            VVector userOrigin = image.UserOrigin;
            //if the user requested to add flash to the plan, be sure to grab the ptv_body_flash structure (i.e., the ptv_body structure created from the body with added flash). 
            //This structure is named 'TS_FLASH_TARGET'
            if(useFlash) target = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "ts_flash_target");
            else target = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "ptv_body");

            //matchline is present and not empty
            if ((selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any()) && !(selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").IsEmpty))
            {

                //5-11-2020 update EAS. isoSeparationSup is the isocenter separation for the VMAT isos and isoSeparationInf is the iso separation for the AP/PA isocenters
                double isoSeparationSup = Math.Round(((target.MeshGeometry.Positions.Max(p => p.Z) - selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - 380.0) / (numVMATIsos-1)) / 10.0f) * 10.0f;
                double isoSeparationInf = Math.Round((selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - target.MeshGeometry.Positions.Min(p => p.Z) - 380.0) / 10.0f) * 10.0f;
                if (isoSeparationSup > 380.0 || isoSeparationInf > 380.0)
                {
                    var CUI = new VMATTBIautoPlan.confirmUI();
                    CUI.message.Text = "Calculated isocenter separation > 38.0 cm, which reduces the overlap between adjacent fields!" + Environment.NewLine + Environment.NewLine + "Truncate isocenter separation to 38.0 cm?!";
                    CUI.button1.Text = "No";
                    CUI.ShowDialog();
                    if (CUI.confirm)
                    {
                        if (isoSeparationSup > 380.0 && isoSeparationInf > 380.0) isoSeparationSup = isoSeparationInf = 380.0;
                        else if (isoSeparationSup > 380.0) isoSeparationSup = 380.0;
                        else isoSeparationInf = 380.0;
                    }
                }

                double matchlineZ = selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z;
                for (int i = 0; i < numVMATIsos; i++)
                {
                    VVector v = new VVector();
                    v.x = userOrigin.x;
                    v.y = userOrigin.y;
                    //6-10-2020 EAS, want to count up from matchplane to ensure distance from matchplane is fixed at 190 mm
                    v.z = matchlineZ + i * isoSeparationSup + 190.0;
                    //round z position to the nearest integer
                    v = plan.StructureSet.Image.DicomToUser(v, plan);
                    v.z = Math.Round(v.z / 10.0f) * 10.0f;
                    v = plan.StructureSet.Image.UserToDicom(v, plan);
                    iso.Add(v);
                }

                //6-10-2020 EAS, need to reverse order of list because it has to be descending from z location (i.e., sup to inf) for beam placement to work correctly
                iso.Reverse();
                //6-11-2020 EAS, this is used to account for any rounding of the isocenter position immediately superior to the matchline
                double offset = iso.LastOrDefault().z - matchlineZ;

                for (int i = 0; i < (numIsos - numVMATIsos); i++)
                {
                    VVector v = new VVector();
                    v.x = userOrigin.x;
                    v.y = userOrigin.y;
                    //5-11-2020 update EAS (the first isocenter immediately inferior to the matchline is now a distance = offset away). This ensures the isocenters immediately inferior and superior to the 
                    //matchline are equidistant from the matchline
                    v.z = matchlineZ - i * isoSeparationInf - offset;
                    //round z position to the nearest integer
                    v = plan.StructureSet.Image.DicomToUser(v, plan);
                    v.z = Math.Round(v.z / 10.0f) * 10.0f;
                    v = plan.StructureSet.Image.UserToDicom(v, plan);
                    iso.Add(v);
                }
            }
            else
            {
                //All VMAT portions of the plans will ONLY have 3 isocenters
                //double isoSeparation = Math.Round(((target.MeshGeometry.Positions.Max(p => p.Z) - target.MeshGeometry.Positions.Min(p => p.Z) - 10.0*numIsos) / numIsos) / 10.0f) * 10.0f;
                //5-7-202 The equation below was determined assuming each VMAT plan would always use 3 isos. In addition, the -30.0 was empirically determined by comparing the calculated isocenter separations to those that were used in the clinical plans
                //isoSeparation = Math.Round(((target.MeshGeometry.Positions.Max(p => p.Z) - target.MeshGeometry.Positions.Min(p => p.Z) - 30.0) / 3) / 10.0f) * 10.0f;

                //however, the actual correct equation is given below:
                isoSeparation = Math.Round(((target.MeshGeometry.Positions.Max(p => p.Z) - target.MeshGeometry.Positions.Min(p => p.Z) - 380.0) / (numVMATIsos - 1)) / 10.0f) * 10.0f;

                //It is calculated by setting the most superior and inferior isocenters to be 19.0 cm from the target volume edge in the z-direction. The isocenter separtion is then calculated as half the distance between these two isocenters (sep = ((max-19cm)-(min+19cm)/2).
                //Tested on 5-7-2020. When the correct equation is rounded, it gives the same answer as the original empirical equation above, however, the isocenters are better positioned in the target volume (i.e., more symmetric about the target volume). 
                //The ratio of the actual to empirical iso separation equations can be expressed as r=(3/(numVMATIsos-1))((x-380)/(x-30)) where x = (max-min). The ratio is within +/-5% for max-min values (i.e., patient heights) between 99.0 cm (i.e., 3.25 feet) and 116.0 cm

                if (isoSeparation > 380.0)
                {
                    var CUI = new VMATTBIautoPlan.confirmUI();
                    CUI.message.Text = "Calculated isocenter separation > 38.0 cm, which reduces the overlap between adjacent fields!" + Environment.NewLine + Environment.NewLine + "Truncate isocenter separation to 38.0 cm?!";
                    CUI.ShowDialog();
                    if (CUI.confirm) isoSeparation = 380.0;
                }

                for (int i = 0; i < numIsos; i++)
                {
                    VVector v = new VVector();
                    v.x = userOrigin.x;
                    v.y = userOrigin.y;
                    //5-7-2020 isocenter positions for actual isocenter separation equation described above
                    v.z = (target.MeshGeometry.Positions.Max(p => p.Z) - i * isoSeparation - 190.0);
                    //round z position to the nearest integer
                    v = plan.StructureSet.Image.DicomToUser(v, plan);
                    v.z = Math.Round(v.z / 10.0f) * 10.0f;
                    v = plan.StructureSet.Image.UserToDicom(v, plan);
                    iso.Add(v);
                }
            }

            //evaluate the distance between the edge of the beam and the max/min of the PTV_body contour. If it is < checkIsoPlacementLimit, then warn the user that they might be fully covering the ptv_body structure.
            //7-17-2020, checkIsoPlacementLimit = 5 mm
            VVector firstIso = iso.First();
            VVector lastIso = iso.Last();
            if (!((firstIso.z + 200.0) - target.MeshGeometry.Positions.Max(p => p.Z) >= checkIsoPlacementLimit) ||
                !(target.MeshGeometry.Positions.Min(p => p.Z) - (lastIso.z - 200.0) >= checkIsoPlacementLimit)) checkIsoPlacement = true;

            //MessageBox.Show(String.Format("{0}, {1}, {2}, {3}, {4}, {5}",
            //    firstIso.z,
            //    lastIso.z,
            //    target.MeshGeometry.Positions.Max(p => p.Z),
            //    target.MeshGeometry.Positions.Min(p => p.Z),
            //    (firstIso.z + 200.0 - target.MeshGeometry.Positions.Max(p => p.Z)),
            //    (target.MeshGeometry.Positions.Min(p => p.Z) - (lastIso.z - 200.0))));

            return iso;
        }

        //function used to cnotour the overlap between fields in adjacent isocenters for the VMAT Plan ONLY!
        //this option is requested by the user by selecting the checkbox on the main UI on the beam placement tab
        private void contourFieldOverlap(List<VVector> isoLocations)
        {
            //grab the image and get the z resolution and dicom origin (we only care about the z position of the dicom origin)
            Image image = selectedSS.Image;
            double zResolution = image.ZRes;
            VVector dicomOrigin = image.Origin;
            //center position between adjacent isocenters, number of image slices to contour on, start image slice location for contouring
            List<Tuple<double,int,int>> overlap = new List<Tuple<double, int, int>> { };
            
            //calculate the center position between adjacent isocenters, number of image slices to contour on based on overlap and with additional user-specified margin (from main UI)
            //and the slice where the contouring should begin
            //string output = "";
            for (int i = 1; i < numVMATIsos; i++)
            {
                //calculate the center position between adjacent isocenters. NOTE: this calculation works from superior to inferior!
                double center = isoLocations.ElementAt(i - 1).z + (isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z) / 2;
                //this is left as a double so I can cast it to an int in the second overlap item and use it in the calculation in the third overlap item
                double numSlices = Math.Ceiling(400.0 + contourOverlapMargin - Math.Abs(isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z));
                overlap.Add(new Tuple<double, int, int>(
                    center,
                    (int)(numSlices / zResolution),
                    (int)(Math.Abs(dicomOrigin.z - center + numSlices / 2) / zResolution)));
                //add a new junction structure (named TS_jnx<i>) to the stack. Contours will be added to these structure later
                jnxs.Add(selectedSS.AddStructure("CONTROL", string.Format("TS_jnx{0}", i)));
                //output += String.Format("{0}, {1}, {2}\n", 
                //    isoLocations.ElementAt(i - 1).z + (isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z) / 2,
                //    (int)Math.Ceiling((410.0 - Math.Abs(isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z)) / zResolution),
                //    (int)(Math.Abs(dicomOrigin.z - (isoLocations.ElementAt(i - 1).z + ((isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z) / 2)) + Math.Ceiling((410.0 - Math.Abs(isoLocations.ElementAt(i).z - isoLocations.ElementAt(i - 1).z))/2)) / zResolution));
            }
            //MessageBox.Show(output);

            //make a box at the min/max x,y positions of the target structure with 5 cm margin
            Point3DCollection targetPts = target.MeshGeometry.Positions;
            double xMax = targetPts.Max(p => p.X) + 50.0;
            double xMin = targetPts.Min(p => p.X) - 50.0;
            double yMax = targetPts.Max(p => p.Y) + 50.0;
            double yMin = targetPts.Min(p => p.Y) - 50.0;

            VVector[] pts = new[] {
                                    new VVector(xMax, yMax, 0),
                                    new VVector(xMax, 0, 0),
                                    new VVector(xMax, yMin, 0),
                                    new VVector(0, yMin, 0),
                                    new VVector(xMin, yMin, 0),
                                    new VVector(xMin, 0, 0),
                                    new VVector(xMin, yMax, 0),
                                    new VVector(0, yMax, 0)};

            //add the contours to each relevant plan for each structure in the jnxs stack
            int count = 0;
            foreach (Tuple<double,int,int> value in overlap)
            {
                for (int i = value.Item3; i < (value.Item3 + value.Item2); i++) jnxs.ElementAt(count).AddContourOnImagePlane(pts, i);
                //only keep the portion of the box contour that overlaps with the target
                jnxs.ElementAt(count).SegmentVolume = jnxs.ElementAt(count).And(target.Margin(0));
                count++;
            }
        }

        private void set_beams(List<VVector> isoLocations)
        {
            //DRR parameters (dummy parameters to generate DRRs for each field)
            DRRCalculationParameters DRR = new DRRCalculationParameters();
            DRR.DRRSize = 500.0;
            DRR.FieldOutlines = true;
            DRR.StructureOutlines = true;
            DRR.SetLayerParameters(1, 1.0, 100.0, 1000.0);

            //place the beams for the VMAT plan
            //unfortunately, all of Nataliya's requirements for beam placement meant that this process couldn't simply draw from beam placement templates. Some of the beam placements for specific isocenters
            //and under certain conditions needed to be hard-coded into the script. I'm not really a fan of this, but it was the only way to satisify Nataliya's requirements.
            int count = 0;
            string beamName;
            VRect<double> jp;
            for (int i = 0; i < numVMATIsos; i++)
            {
                for (int j = 0; j < numBeams[i]; j++)
                {
                    //second isocenter and third beam requires the x-jaw positions to be mirrored about the y-axis (these jaw positions are in the fourth element of the jawPos list)
                    //this is generally the isocenter located in the pelvis and we want the beam aimed at the kidneys-area
                    if (i == 1 && j == 2) jp = jawPos.ElementAt(j + 1);
                    else if (i == 1 && j == 3) jp = jawPos.ElementAt(j - 1);
                    else jp = jawPos.ElementAt(j);
                    Beam b;
                    beamName = "";
                    beamName += String.Format("{0} ", count + 1);
                    //zero collimator rotations of two main fields for beams in isocenter immediately superior to matchline. Adjust the third beam such that collimator rotation is 90 degrees. Do not adjust 4th beam
                    double coll = collRot[j];
                    if ((numIsos > numVMATIsos) && (i == (numVMATIsos - 1)))
                    {
                        if (j < 2) coll = 0.0;
                        else if (j == 2) coll = 90.0;
                    }
                    //all even beams (e.g., 2, 4, etc.) will be CCW and all odd beams will be CW
                    if (count % 2 == 0)
                    {
                        b = plan.AddArcBeam(ebmpArc, jp, coll, CCW[0], CCW[1], GantryDirection.CounterClockwise, 0, isoLocations.ElementAt(i));
                        if (j >= 2) beamName += String.Format("CCW {0}{1}", isoNames.ElementAt(i), 90);
                        else beamName += String.Format("CCW {0}{1}", isoNames.ElementAt(i), "");
                    }
                    else
                    {
                        b = plan.AddArcBeam(ebmpArc, jp, coll, CW[0], CW[1], GantryDirection.Clockwise, 0, isoLocations.ElementAt(i));
                        if (j >= 2) beamName += String.Format("CW {0}{1}", isoNames.ElementAt(i), 90);
                        else beamName += String.Format("CW {0}{1}", isoNames.ElementAt(i), "");
                    }
                    b.Id = beamName;
                    b.CreateOrReplaceDRR(DRR);
                    count++;
                }
            }

            //add additional plan for ap/pa legs fields (all ap/pa isocenter fields will be contained within this plan)
            if (numIsos > numVMATIsos)
            {
                //6-10-2020 EAS, checked if exisiting _Legs plan is present in createPlan method
                legs_planUpper = tbi.AddExternalPlanSetup(selectedSS);
                if(singleAPPAplan) legs_planUpper.Id = String.Format("_Legs");
                else legs_planUpper.Id = String.Format("{0} Upper Legs", numVMATIsos + 1);
                //100% dose prescribed in plan
                legs_planUpper.SetPrescription(prescription.Item1, prescription.Item2, 1.0);
                legs_planUpper.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);

                Structure target;
                if (useFlash) target = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "ts_flash_target");
                else target = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "ptv_body");

                //adjust x2 jaw (furthest from matchline) so that it covers edge of target volume
                double x2 = isoLocations.ElementAt(numVMATIsos).z - (target.MeshGeometry.Positions.Min(p => p.Z) - 20.0);
                if (x2 > 200.0) x2 = 200.0;
                else if (x2 < 10.0) x2 = 10.0;

                //AP field
                //set MLC positions. First row is bank number 0 (X1 leaves) and second row is bank number 1 (X2).
                float[,] MLCpos = new float[2, 60];
                for (int i = 0; i < 60; i++)
                {
                    MLCpos[0, i] = (float)-200.0;
                    MLCpos[1, i] = (float)(x2);
                }
                Beam b = legs_planUpper.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(-200.0, -200.0, x2, 200.0), 90.0, 0.0, 0.0, isoLocations.ElementAt(numVMATIsos));
                b.Id = String.Format("{0} AP Upper Legs", ++count);
                b.CreateOrReplaceDRR(DRR);

                //PA field
                b = legs_planUpper.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(-200.0, -200.0, x2, 200.0), 90.0, 180.0, 0.0, isoLocations.ElementAt(numVMATIsos));
                b.Id = String.Format("{0} PA Upper Legs", ++count);
                b.CreateOrReplaceDRR(DRR);

                if((numIsos - numVMATIsos) == 2)
                {
                    VVector infIso = new VVector();
                    //the element at numVMATIsos in isoLocations vector is the first AP/PA isocenter
                    infIso.x = isoLocations.ElementAt(numVMATIsos).x;
                    infIso.y = isoLocations.ElementAt(numVMATIsos).y;

                    double x1 = -200.0;
                    //if the distance between the matchline and the inferior edge of the target is < 600 mm, set the beams in the second isocenter (inferior-most) to be half-beam blocks
                    if (selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - target.MeshGeometry.Positions.Min(p => p.Z) < 600.0)
                    {
                        infIso.z = isoLocations.ElementAt(numVMATIsos).z - 200.0;
                        x1 = 0.0;
                    }
                    else infIso.z = isoLocations.ElementAt(numVMATIsos).z - 390.0;
                    //fit x1 jaw to extend of patient
                    x2 = infIso.z - (target.MeshGeometry.Positions.Min(p => p.Z) - 20.0);
                    if (x2 > 200.0) x2 = 200.0;
                    else if (x2 < 10.0) x2 = 10.0;

                    //set MLC positions
                    MLCpos = new float[2,60];
                    for (int i = 0; i < 60; i++)
                    {
                        MLCpos[0, i] = (float)(x1);
                        MLCpos[1, i] = (float)(x2);
                    }
                    //AP field
                    if (singleAPPAplan)
                    {
                        b = legs_planUpper.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(x1, -200.0, x2, 200.0), 90.0, 0.0, 0.0, infIso);
                        b.Id = String.Format("{0} AP Lower Legs", ++count);
                        b.CreateOrReplaceDRR(DRR);

                        //PA field
                        b = legs_planUpper.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(x1, -200.0, x2, 200.0), 90.0, 180.0, 0.0, infIso);
                        b.Id = String.Format("{0} PA Lower Legs", ++count);
                        b.CreateOrReplaceDRR(DRR);
                    }
                    else
                    {
                        //create a new legs plan if the user wants to separate the two APPA isocenters into separate plans
                        ExternalPlanSetup legs_planLower = tbi.AddExternalPlanSetup(selectedSS);
                        legs_planLower.Id = String.Format("{0} Lower Legs", numIsos);
                        legs_planLower.SetPrescription(prescription.Item1, prescription.Item2, 1.0);
                        legs_planLower.SetCalculationModel(CalculationType.PhotonVolumeDose, calculationModel);

                        b = legs_planLower.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(x1, -200.0, x2, 200.0), 90.0, 0.0, 0.0, infIso);
                        b.Id = String.Format("{0} AP Lower Legs", ++count);
                        b.CreateOrReplaceDRR(DRR);

                        //PA field
                        b = legs_planLower.AddMLCBeam(ebmpStatic, MLCpos, new VRect<double>(x1, -200.0, x2, 200.0), 90.0, 180.0, 0.0, infIso);
                        b.Id = String.Format("{0} PA Lower Legs", ++count);
                        b.CreateOrReplaceDRR(DRR);
                    }
                }
            }
            MessageBox.Show("Beams placed successfully!\nPlease proceed to the optimization setup tab!");
        }
    }
}
