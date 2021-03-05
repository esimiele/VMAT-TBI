using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media.Media3D;
using System.IO;
using Microsoft.Win32;

namespace VMATTBIautoPlan
{
    class generateTS
    {
        public List<Tuple<string, string, double>> spareStructList = new List<Tuple<string, string, double>> { };
        StructureSet selectedSS;
        //DICOM types
        //Possible values are "AVOIDANCE", "CAVITY", "CONTRAST_AGENT", "CTV", "EXTERNAL", "GTV", "IRRAD_VOLUME", 
        //"ORGAN", "PTV", "TREATED_VOLUME", "SUPPORT", "FIXATION", "CONTROL", and "DOSE_REGION". 
        List<Tuple<string, string>> TS_structures = new List<Tuple<string, string>>
        { Tuple.Create("CONTROL", "Human_Body"),
          Tuple.Create("CONTROL","Lungs-1cm"),
          Tuple.Create("CONTROL","Lungs-2cm"),
          Tuple.Create("CONTROL","Liver-1cm"),
          Tuple.Create("CONTROL","Liver-2cm"),
          Tuple.Create("CONTROL","Kidneys-1cm"),
          Tuple.Create("CONTROL","Brain-0.5cm"),
          Tuple.Create("CONTROL","Brain-1cm"),
          Tuple.Create("CONTROL","Brain-2cm"),
          Tuple.Create("CONTROL","Brain-3cm"),
          Tuple.Create("PTV","PTV_Body"),
          Tuple.Create("CONTROL","TS_PTV_VMAT")
        };
        List<Tuple<string, string>> scleroStructures = new List<Tuple<string, string>>
        {
            Tuple.Create("CONTROL","Lung_Block_L"),
            Tuple.Create("CONTROL","Lung_Block_R"),
            Tuple.Create("CONTROL","Lungs_Eval"),
            Tuple.Create("CONTROL","Kidney_Block_L"),
            Tuple.Create("CONTROL","Kidney_Block_R")
        };
        List<string> addedStructures = new List<string> { };
        public List<Tuple<string, string>> optParameters = new List<Tuple<string, string>> { };
        public int numIsos;
        public int numVMATIsos;
        public List<string> isoNames = new List<string> { };
        private double targetMargin;
        private bool scleroTrial;
        private bool useFlash = false;
        private Structure flashStructure = null;
        private double flashMargin;
        public bool updateSparingList = false;

        public generateTS(List<Tuple<string, string, double>> list, StructureSet ss, double tm, bool st)
        {
            spareStructList = list;
            selectedSS = ss;
            targetMargin = tm;
            scleroTrial = st;
        }

        public generateTS(List<Tuple<string, string, double>> list, StructureSet ss, double tm, bool st, bool flash, Structure fSt, double fM)
        {
            //overloaded constructor for the case where the user wants to include flash in the simulation
            spareStructList = list;
            selectedSS = ss;
            targetMargin = tm;
            scleroTrial = st;
            useFlash = flash;
            flashStructure = fSt;
            flashMargin = fM;
        }

        public bool generateStructures()
        {
            isoNames.Clear();
            if (preliminaryChecks()) return true;
            if (createTSStructures()) return true;
            if (useFlash) if(createFlash()) return true;
            MessageBox.Show("Structures generated successfully!\nPlease proceed to the beam placement tab!");
            return false;
        }

        private bool preliminaryChecks()
        {
            //check if user origin was set
            //get the points collection for the Body (used for calculating number of isocenters)
            Point3DCollection pts = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "body").MeshGeometry.Positions;
            if (!selectedSS.Image.HasUserOrigin || !(selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "body").IsPointInsideSegment(selectedSS.Image.UserOrigin)))
            {
                MessageBox.Show("Did you forget to set the user origin? \nUser origin is NOT inside body contour! \nPlease fix and try again!");
                return true;
            }

            //check if patient length is > 116cm, if so, check for matchline contour
            if ((pts.Max(p => p.Z) - pts.Min(p => p.Z)) > 1160.0 && !(selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any()))
            {
                //check to see if the user wants to proceed even though there is no matchplane contour or the matchplane contour exists, but is not filled
                VMATTBIautoPlan.confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                CUI.message.Text = "No matchplane contour found even though patient length > 116.0 cm!" + Environment.NewLine + Environment.NewLine + "Continue?!";
                CUI.ShowDialog();
                if (!CUI.confirm) return true;

                //checks for LA16 couch and spinning manny couch/bolt will be performed at optimization stage
            }

            //calculate number of required isocenters
            if (!(selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any()))
            {
                //no matchline implying that this patient will be treated with VMAT only. For these cases the maximum number of allowed isocenters is 3.
                //the reason for the explicit statements calculating the number of isos and then truncating them to 3 was to account for patients requiring < 3 isos and if, later on, we want to remove the restriction of 3 isos
                numIsos = numVMATIsos = (int)Math.Ceiling(((pts.Max(p => p.Z) - pts.Min(p => p.Z)) / (400.0 - 20.0)));
                if (numIsos > 3) numIsos = numVMATIsos = 3;
            }
            else
            {
                //matchline structure is present, but empty
                if (selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").IsEmpty)
                {
                    VMATTBIautoPlan.confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                    CUI.message.Text = "I found a matchline structure in the structure set, but it's empty!" + Environment.NewLine + Environment.NewLine + "Do you want to continue without using the matchline structure?!";
                    CUI.ShowDialog();
                    if (!CUI.confirm) return true;

                    //continue and ignore the empty matchline structure (same calculation as VMAT only)
                    numIsos = numVMATIsos = (int)Math.Ceiling(((pts.Max(p => p.Z) - pts.Min(p => p.Z)) / (400.0 - 20.0)));
                    if (numIsos > 3) numIsos = numVMATIsos = 3;
                }
                //matchline structure is present and not empty
                else
                {
                    //get number of isos for PTV superior to matchplane (always truncate this value to a maximum of 3 isocenters)
                    numVMATIsos = (int)Math.Ceiling(((pts.Max(p => p.Z) - selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z) / (400.0 - 20.0)));
                    if (numVMATIsos > 3) numVMATIsos = 3;

                    //get number of iso for PTV inferior to matchplane
                    //if (selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - pts.Min(p => p.Z) - 3.0 <= (400.0 - 20.0)) numIsos = numVMATIsos + 1;

                    //5-20-2020 Nataliya said to only add a second legs iso if the extent of the TS_PTV_LEGS is > 40.0 cm
                    if (selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - pts.Min(p => p.Z) - 3.0 <= (400.0 - 0.0)) numIsos = numVMATIsos + 1;
                    else numIsos = numVMATIsos + 2;
                    //MessageBox.Show(String.Format("{0}", selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").CenterPoint.z - pts.Min(p => p.Z) - 3.0));
                }
            }

            //set isocenter names based on numIsos and numVMATIsos (determined these names from prior cases). Need to implement a more clever way to name the isocenters
            isoNames.Add("Head");
            if (numVMATIsos == 2) isoNames.Add("Pelvis");
            else if (numVMATIsos == 3 && numIsos > numVMATIsos)
            {
                isoNames.Add("Chest"); isoNames.Add("Pelvis");
            }
            //this could technically be an else statement, but I left it as an else-if statement so it's explicit what situation is being considered here
            else if (numVMATIsos == 3 && numIsos == numVMATIsos)
            {
                isoNames.Add("Pelvis"); isoNames.Add("Legs");
            }
            if (numIsos > numVMATIsos)
            {
                isoNames.Add("AP / PA upper legs");
                if (numIsos == numVMATIsos + 2) isoNames.Add("AP / PA lower legs");
            }

            //check if selected structures are empty or of high-resolution (i.e., no operations can be performed on high-resolution structures)
            string output = "The following structures are high-resolution:" + System.Environment.NewLine;
            List<Structure> highResStructList = new List<Structure> { };
            List<Tuple<string, string, double>> highResSpareList = new List<Tuple<string, string, double>> { };
            foreach (Tuple<string, string, double> itr in spareStructList)
            {
                if (itr.Item2 == "Mean Dose < Rx Dose")
                {
                    if (selectedSS.Structures.First(x => x.Id == itr.Item1).IsEmpty)
                    {
                        MessageBox.Show(String.Format("Error! \nThe selected structure that will be subtracted from PTV_Body and TS_PTV_VMAT is empty! \nContour the structure and try again."));
                        return true;
                    }
                    else if (selectedSS.Structures.First(x => x.Id == itr.Item1).IsHighResolution)
                    {
                        highResStructList.Add(selectedSS.Structures.First(x => x.Id == itr.Item1));
                        highResSpareList.Add(itr);
                        output += String.Format("{0}", itr.Item1) + System.Environment.NewLine;
                    }
                }
            }
            //if there are high resolution structures, they will need to be converted to default resolution.
            if (highResStructList.Count() > 0)
            {
                //ask user if they are ok with converting the relevant high resolution structures to default resolution
                output += "They must be converted to default resolution before proceeding!";
                VMATTBIautoPlan.confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                CUI.message.Text = output + Environment.NewLine + Environment.NewLine + "Continue?!";
                CUI.message.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
                CUI.ShowDialog();
                if (!CUI.confirm) return true;

                int count = 0;
                foreach (Structure s in highResStructList)
                {
                    //get the high res structure mesh geometry
                    MeshGeometry3D mesh = s.MeshGeometry;
                    //get the start and stop image planes for this structure
                    int startSlice = (int)((mesh.Bounds.Z - selectedSS.Image.Origin.z) / selectedSS.Image.ZRes);
                    int stopSlice = (int)(((mesh.Bounds.Z + mesh.Bounds.SizeZ) - selectedSS.Image.Origin.z) / selectedSS.Image.ZRes) + 1;
                    //create an Id for the low resolution struture that will be created. The name will be '_lowRes' appended to the current structure Id
                    string newName = s.Id + "_lowRes";
                    if (newName.Length > 16) newName = newName.Substring(0, 16);
                    //add a new structure (default resolution by default)
                    Structure lowRes = null;
                    if (selectedSS.CanAddStructure("CONTROL", newName)) lowRes = selectedSS.AddStructure("CONTROL", newName);
                    else
                    {
                        MessageBox.Show(String.Format("Error! Cannot add new structure: {0}!\nCorrect this issue and try again!", newName.Substring(0, 16)));
                        return true;
                    }

                    //foreach slice that contains contours, get the contours, and determine if you need to add or subtract the contours on the given image plane for the new low resolution structure. You need to subtract contours if the points lie INSIDE the current structure contour.
                    //We can sample three points (first, middle, and last points in array) to see if they are inside the current contour. If any of them are, subtract the set of contours from the image plane. Otherwise, add the contours to the image plane. NOTE: THIS LOGIC ASSUMES
                    //THAT YOU DO NOT OBTAIN THE CUTOUT CONTOUR POINTS BEFORE THE OUTER CONTOUR POINTS (it seems that ESAPI generally passes the main structure contours first before the cutout contours, but more testing is needed)
                    //string data = "";
                    for (int slice = startSlice; slice < stopSlice; slice++)
                    {
                        VVector[][] points = s.GetContoursOnImagePlane(slice);
                        for (int i = 0; i < points.GetLength(0); i++)
                        {
                            //for (int j = 0; j < points[i].GetLength(0); j++) data += String.Format("{0}, {1}, {2}", points[i][j].x, points[i][j].y, points[i][j].z) + System.Environment.NewLine;
                            if(lowRes.IsPointInsideSegment(points[i][0]) || lowRes.IsPointInsideSegment(points[i][points[i].GetLength(0) - 1]) || lowRes.IsPointInsideSegment(points[i][(int)(points[i].GetLength(0)/2)])) lowRes.SubtractContourOnImagePlane(points[i], slice);
                            else lowRes.AddContourOnImagePlane(points[i], slice);
                            //data += System.Environment.NewLine;
                        }
                    }
                    
                    //get the index of the high resolution structure in the structure sparing list and repace this entry with the newly created low resolution structure
                    int index = spareStructList.IndexOf(highResSpareList.ElementAt(count));
                    spareStructList.RemoveAt(index);
                    spareStructList.Insert(index, new Tuple<string, string, double>(newName, highResSpareList.ElementAt(count).Item2, highResSpareList.ElementAt(count).Item3));
                    count++;

                    //data += System.Environment.NewLine;
                    //points = lowRes.GetContoursOnImagePlane((int)((stopSlice + startSlice) / 2));
                    //for (int i = 0; i < points.GetLength(0); i++)
                    //{
                    //    for (int j = 0; j < points[i].GetLength(0); j++) data += String.Format("{0}, {1}, {2}", points[i][j].x, points[i][j].y, points[i][j].z) + System.Environment.NewLine;
                    //    //data += System.Environment.NewLine;
                    //}

                    //File.WriteAllText(@"\\enterprise.stanfordmed.org\depts\RadiationTherapy\Public\Users\ESimiele\vvectorData.txt", data);
                }
                //inform the main UI class that the UI needs to be updated
                updateSparingList = true;
                
                //string mod = "";
                //foreach (Tuple<string, string, double> itr in spareStructList) mod += String.Format("{0}, {1}, {2}", itr.Item1, itr.Item2, itr.Item3) + System.Environment.NewLine;
                //MessageBox.Show(test);
                //MessageBox.Show(mod);
            }
            return false;
        }

        private bool createTSStructures()
        {
            if(RemoveOldTSStructures(TS_structures)) return true;

            if(scleroTrial) if(RemoveOldTSStructures(scleroStructures)) return true;

            //determine if any TS structures need to be added to the selected structure set (i.e., were not present or were removed in the first foreach loop)
            //this is provided here to only add additional TS if they are relevant to the current case (i.e., it doesn't make sense to add the brain TS's if we 
            //are not interested in sparing brain)
            foreach (Tuple<string, string, double> itr in spareStructList)
            {
                optParameters.Add(Tuple.Create(itr.Item1, itr.Item2));
                if (itr.Item1.ToLower().Contains("lungs"))
                {
                    foreach (Tuple<string, string> itr1 in TS_structures.Where(x => x.Item2.ToLower().Contains("lungs"))) AddTSStructures(itr1);
                    //do NOT add the scleroStructures to the addedStructures vector as these will be handled manually!
                    if (scleroTrial)
                    {
                        if (selectedSS.CanAddStructure("CONTROL", "Lung_Block_L")) selectedSS.AddStructure("CONTROL", "Lung_Block_L");
                        if (selectedSS.CanAddStructure("CONTROL", "Lung_Block_R")) selectedSS.AddStructure("CONTROL", "Lung_Block_R");
                        if (selectedSS.CanAddStructure("CONTROL", "Lungs_Eval")) selectedSS.AddStructure("CONTROL", "Lungs_Eval");
                    }
                }
                else if (itr.Item1.ToLower().Contains("liver")) foreach (Tuple<string, string> itr1 in TS_structures.Where(x => x.Item2.ToLower().Contains("liver"))) AddTSStructures(itr1);
                else if (itr.Item1.ToLower().Contains("brain")) foreach (Tuple<string, string> itr1 in TS_structures.Where(x => x.Item2.ToLower().Contains("brain"))) AddTSStructures(itr1);
                else if (itr.Item1.ToLower().Contains("kidneys"))
                {
                    foreach (Tuple<string, string> itr1 in TS_structures.Where(x => x.Item2.ToLower().Contains("kidneys"))) AddTSStructures(itr1);
                    //do NOT add the scleroStructures to the addedStructures vector as these will be handled manually!
                    if (scleroTrial)
                    {
                        if (selectedSS.CanAddStructure("CONTROL", "Kidney_Block_R")) selectedSS.AddStructure("CONTROL", "Kidney_Block_R");
                        if (selectedSS.CanAddStructure("CONTROL", "Kidney_Block_L")) selectedSS.AddStructure("CONTROL", "Kidney_Block_L");
                    }
                }
            }

            if (scleroTrial)
            {
                foreach (Tuple<string, string> itr in scleroStructures)
                {
                    Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == itr.Item2.ToLower());
                    Structure tmp1 = null;
                    if (itr.Item2.ToLower().Contains("lung_block_l"))
                    {

                        //AxisAlignedMargins(inner or outer margin, margin from negative x, margin for negative y, margin for negative z, margin for positive x, margin for positive y, margin for positive z)
                        tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "lung_l");
                        if (tmp1 != null) tmp.SegmentVolume = tmp1.AsymmetricMargin(new AxisAlignedMargins(
                                                                                                   StructureMarginGeometry.Inner,
                                                                                                   10.0,
                                                                                                   10.0,
                                                                                                   15.0,
                                                                                                   10.0,
                                                                                                   10.0,
                                                                                                   10.0));
                    }
                    else if (itr.Item2.ToLower().Contains("lung_block_r"))
                    {
                        tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "lung_r");
                        if (tmp1 != null) tmp.SegmentVolume = tmp1.AsymmetricMargin(new AxisAlignedMargins(
                                                                                                   StructureMarginGeometry.Inner,
                                                                                                   10.0,
                                                                                                   10.0,
                                                                                                   15.0,
                                                                                                   10.0,
                                                                                                   10.0,
                                                                                                   10.0));
                    }
                    else if (itr.Item2.ToLower().Contains("lungs_eval"))
                    {
                        tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "lung_block_l");
                        Structure tmp2 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "lung_block_r");
                        if (tmp1 != null && tmp2 != null) tmp.SegmentVolume = tmp2.Or(tmp1.Margin(0.0));
                    }
                    else if (itr.Item2.ToLower().Contains("kidney_block_l"))
                    {
                        tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "kidney_l");
                        if (tmp1 != null) tmp.SegmentVolume = tmp1.AsymmetricMargin(new AxisAlignedMargins(
                                                                                                   StructureMarginGeometry.Outer,
                                                                                                   5.0,
                                                                                                   20.0,
                                                                                                   20.0,
                                                                                                   20.0,
                                                                                                   20.0,
                                                                                                   20.0));
                    }
                    else
                    {
                        tmp1 = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "kidney_r");
                        if (tmp1 != null) tmp.SegmentVolume = tmp1.AsymmetricMargin(new AxisAlignedMargins(
                                                                                                   StructureMarginGeometry.Outer,
                                                                                                   20.0,
                                                                                                   20.0,
                                                                                                   20.0,
                                                                                                   5.0,
                                                                                                   20.0,
                                                                                                   20.0));
                    }
                }
            }

            //now contour the various structures
            foreach (string s in addedStructures)
            {
                Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == s.ToLower());
                //MessageBox.Show(s);
                if (!(s.ToLower().Contains("ptv")))
                {
                    Structure tmp1 = null;
                    double margin = 0.0;
                    if (s.ToLower().Contains("human")) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "body");
                    else if (s.ToLower().Contains("lungs")) if (selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "lungs_lowres") == null) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "lungs"); else tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "lungs_lowres");
                    else if (s.ToLower().Contains("liver")) if (selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "liver_lowres") == null) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "liver"); else tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "liver_lowres");
                    else if (s.ToLower().Contains("kidneys")) if (selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "kidneys_lowres") == null) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidneys"); else tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidneys_lowres");
                    else if (s.ToLower().Contains("brain")) if (selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == "brain_lowres") == null) tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "brain"); else tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "brain_lowres");

                    //all structures in TS_structures and scleroStructures are inner margins, which is why the below code works.
                    int pos1 = s.IndexOf("-");
                    int pos2 = s.IndexOf("cm");
                    if (pos1 != -1 && pos2 != -1) double.TryParse(s.Substring(pos1, pos2 - pos1), out margin);

                    //convert from cm to mm
                    tmp.SegmentVolume = tmp1.Margin(margin * 10);
                }
                else if (s.ToLower() == "ptv_body")
                {
                    //get the body contour and create the ptv structure using the user-specified inner margin
                    Structure tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "body");
                    tmp.SegmentVolume = tmp1.Margin(-targetMargin * 10);

                    //subtract all the structures the user wants to spare from PTV_Body
                    foreach (Tuple<string, string, double> spare in spareStructList)
                    {
                        if (spare.Item2 == "Mean Dose < Rx Dose")
                        {
                            if (spare.Item1.ToLower() == "kidneys" && scleroTrial)
                            {
                                tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidney_block_r");
                                tmp.SegmentVolume = tmp.Sub(tmp1.Margin(0.0));
                                tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidney_block_l");
                                tmp.SegmentVolume = tmp.Sub(tmp1.Margin(0.0));
                            }
                            else
                            {
                                tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == spare.Item1.ToLower());
                                tmp.SegmentVolume = tmp.Sub(tmp1.Margin((spare.Item3) * 10));
                            }
                        }
                    }
                }
                else if (s.ToLower() == "ts_ptv_vmat")
                {
                    //copy the ptv_body contour onto the TS_ptv_vmat contour
                    Structure tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "ptv_body");
                    tmp.SegmentVolume = tmp1.Margin(0.0);

                    //matchplane exists and needs to be cut from TS_PTV_Body. Also remove all TS_PTV_Body segements inferior to match plane
                    if (selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any())
                    {
                        //find the image plane where the matchline is location. Record this value and break the loop. Also find the first slice where the ptv_body contour starts and record this value
                        Structure matchline = selectedSS.Structures.First(x => x.Id.ToLower() == "matchline");
                        bool lowLimNotFound = true;
                        int lowLim = -1;
                        if (!matchline.IsEmpty)
                        {
                            int matchplaneLocation = 0;
                            for (int i = 0; i != selectedSS.Image.ZSize - 1; i++)
                            {
                                if (matchline.GetContoursOnImagePlane(i).Any())
                                {
                                    matchplaneLocation = i;
                                    break;
                                }
                                if(lowLimNotFound && tmp1.GetContoursOnImagePlane(i).Any())
                                {
                                    lowLim = i;
                                    lowLimNotFound = false;
                                }
                            }

                            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "dummybox").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "dummybox"));
                            Structure dummyBox = selectedSS.AddStructure("CONTROL", "DummyBox");

                            //get min/max positions of ptv_body contour to contour the dummy box for creating TS_PTV_Legs
                            Point3DCollection ptv_bodyPts = tmp1.MeshGeometry.Positions;
                            double xMax = ptv_bodyPts.Max(p => p.X) + 50.0;
                            double xMin = ptv_bodyPts.Min(p => p.X) - 50.0;
                            double yMax = ptv_bodyPts.Max(p => p.Y) + 50.0;
                            double yMin = ptv_bodyPts.Min(p => p.Y) - 50.0;

                            //box with contour points located at (x,y), (x,0), (x,-y), (0,-y), (-x,-y), (-x,0), (-x, y), (0,y)
                            VVector[] pts = new[] {
                                    new VVector(xMax, yMax, 0),
                                    new VVector(xMax, 0, 0),
                                    new VVector(xMax, yMin, 0),
                                    new VVector(0, yMin, 0),
                                    new VVector(xMin, yMin, 0),
                                    new VVector(xMin, 0, 0),
                                    new VVector(xMin, yMax, 0),
                                    new VVector(0, yMax, 0)};

                            //give 5cm margin on TS_PTV_LEGS (one slice of the CT should be 5mm) in case user wants to include flash up to 5 cm
                            for (int i = matchplaneLocation - 1; i > lowLim - 10; i--) dummyBox.AddContourOnImagePlane(pts, i);

                            //do the structure manipulation
                            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "ts_ptv_legs").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "ts_ptv_legs"));
                            Structure TS_legs = selectedSS.AddStructure("CONTROL", "TS_PTV_Legs");
                            TS_legs.SegmentVolume = dummyBox.And(tmp.Margin(0));
                            //subtract both dummybox and matchline from TS_PTV_VMAT
                            tmp.SegmentVolume = tmp.Sub(dummyBox.Margin(0.0));
                            tmp.SegmentVolume = tmp.Sub(matchline.Margin(0.0));
                            //remove the dummybox structure if flash is NOT being used as its no longer needed
                            if(!useFlash) selectedSS.RemoveStructure(dummyBox);
                        }
                    }
                }
            }
            return false;
        }

        private bool createFlash()
        {
            //create flash for the plan per the users request
            //NOTE: IT IS IMPORTANT THAT ALL OF THE STRUCTURES CREATED IN THIS METHOD (I.E., ALL STRUCTURES USED TO GENERATE FLASH HAVE THE KEYWORD 'FLASH' SOMEWHERE IN THE STRUCTURE ID)!
            //first need to create a bolus structure (remove it if it already exists)
            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "bolus_flash").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "bolus_flash"));
            Structure bolus = selectedSS.AddStructure("CONTROL", "BOLUS_FLASH");
            //if the flashStructure is not null, then the user wants local flash --> use flashStructure. If not, then the user wants global flash --> use body structure
            //add user-specified margin on top of structure of interest to generate flash. 
            //8 -14-2020, an asymmetric margin is used because I might intend to change this code so flash is added in all directions except for sup/inf/post
            if(flashStructure != null) bolus.SegmentVolume = flashStructure.AsymmetricMargin(new AxisAlignedMargins(StructureMarginGeometry.Outer, 
                                                                                                                                flashMargin * 10.0,
                                                                                                                                flashMargin * 10.0,
                                                                                                                                flashMargin * 10.0,
                                                                                                                                flashMargin * 10.0,
                                                                                                                                flashMargin * 10.0,
                                                                                                                                flashMargin * 10.0));
            else bolus.SegmentVolume = selectedSS.Structures.First(x => x.Id.ToLower() == "body").AsymmetricMargin(new AxisAlignedMargins(StructureMarginGeometry.Outer,
                                                                                                                                                  flashMargin * 10.0,
                                                                                                                                                  flashMargin * 10.0,
                                                                                                                                                  flashMargin * 10.0,
                                                                                                                                                  flashMargin * 10.0,
                                                                                                                                                  flashMargin * 10.0,
                                                                                                                                                  flashMargin * 10.0));
            //now subtract the body structure from the newly-created bolus structure
            bolus.SegmentVolume = bolus.Sub(selectedSS.Structures.First(x => x.Id.ToLower() == "body").Margin(0.0));
            //if the subtracted structre is empty, then that indicates that the structure used to generate the flash was NOT close enough to the body surface to generate flash for the user-specified margin
            if(bolus.IsEmpty)
            {
                MessageBox.Show("Error! Created bolus structure does not extrude from the body! \nIs this the right structure?");
                return true;
            }
            //assign the water to the bolus volume (HU = 0.0)
            bolus.SetAssignedHU(0.0);
            //crop flash at matchline ONLY if global flash is used
            Structure dummyBox = null;
            if (flashStructure == null && selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any() && !selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").IsEmpty)
            {
                dummyBox = selectedSS.Structures.First(x => x.Id.ToLower() == "dummybox");
                bolus.SegmentVolume = bolus.Sub(dummyBox.Margin(0.0));
                bolus.SegmentVolume = bolus.Sub(selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").Margin(0.0));
            }
            //Now extend the body contour to include the bolus_flash structure. The reason for this is because Eclipse automatically sets the dose calculation grid to the body structure contour (no overriding this)
            selectedSS.Structures.First(x => x.Id.ToLower() == "body").SegmentVolume = selectedSS.Structures.First(x => x.Id.ToLower() == "body").Or(bolus.Margin(0.0));

            //now create the ptv_flash structure
            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "ts_ptv_flash").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "ts_ptv_flash"));
            Structure ptv_flash = selectedSS.AddStructure("CONTROL", "TS_PTV_FLASH");
            //copy the NEW body structure (i.e., body + bolus_flash) with a 3 mm inner margin
            ptv_flash.SegmentVolume = selectedSS.Structures.First(x => x.Id.ToLower() == "body").Margin(-targetMargin * 10);

            //now subtract all the structures in the spareStructList from ts_ptv_flash (same code as used in the createTSStructures method)
            Structure tmp1;
            foreach (Tuple<string, string, double> spare in spareStructList)
            {
                if (spare.Item2 == "Mean Dose < Rx Dose")
                {
                    if (spare.Item1.ToLower() == "kidneys" && scleroTrial)
                    {
                        tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidney_block_r");
                        ptv_flash.SegmentVolume = ptv_flash.Sub(tmp1.Margin(0.0));
                        tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == "kidney_block_l");
                        ptv_flash.SegmentVolume = ptv_flash.Sub(tmp1.Margin(0.0));
                    }
                    else
                    {
                        tmp1 = selectedSS.Structures.First(x => x.Id.ToLower() == spare.Item1.ToLower());
                        ptv_flash.SegmentVolume = ptv_flash.Sub(tmp1.Margin((spare.Item3) * 10));
                    }
                }
            }

            //copy this structure onto a new structure called TS_FLASH_TARGET. This structure will be analogous to PTV_BODY but for the body that include flash
            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "ts_flash_target").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "ts_flash_target"));
            Structure flash_target = selectedSS.AddStructure("CONTROL", "TS_FLASH_TARGET");
            flash_target.SegmentVolume = ptv_flash.Margin(0.0);

            //now we need to cut ts_ptv_flash at the matchline and remove all contours below the matchline (basically the same process as generating ts_ptv_vmat in the createTSStructures method)
            //need another if-statement here to create ts_legs_flash if necessary
            if (selectedSS.Structures.Where(x => x.Id.ToLower() == "matchline").Any() && !selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").IsEmpty)
            {
                //grab dummy box if not retrieved. There might be a case were local flash was used so the dummy box was NOT retrieved in the previous if-statement
                if(dummyBox == null) dummyBox = selectedSS.Structures.First(x => x.Id.ToLower() == "dummybox");
                //dummy box structure should still exist in the structure set (createTSStructures method does not delete dummy box if the user wants to include flash in the simulation)
                if (selectedSS.Structures.Where(x => x.Id.ToLower() == "ts_legs_flash").Any()) selectedSS.RemoveStructure(selectedSS.Structures.First(x => x.Id.ToLower() == "ts_legs_flash"));
                Structure legs_flash = selectedSS.AddStructure("CONTROL", "TS_LEGS_FLASH");
                legs_flash.SegmentVolume = dummyBox.And(ptv_flash.Margin(0.0));

                //same deal as generating ts_ptv_vmat
                ptv_flash.SegmentVolume = ptv_flash.Sub(dummyBox.Margin(0.0));
                ptv_flash.SegmentVolume = ptv_flash.Sub(selectedSS.Structures.First(x => x.Id.ToLower() == "matchline").Margin(0.0));
                //now you can remove the dummy box structure as it's no longer needed
                selectedSS.RemoveStructure(dummyBox);
            }
            return false;
        }

        private bool RemoveOldTSStructures(List<Tuple<string, string>> structures)
        {
            //remove existing TS structures if they exist and re-add them to the structure list
            foreach (Tuple<string, string> itr in structures)
            {
                Structure tmp = selectedSS.Structures.FirstOrDefault(x => x.Id.ToLower() == itr.Item2.ToLower());

                //structure is present in selected structure set
                if (tmp != null)
                {
                    //check to see if the dicom type is "none"
                    if (!(tmp.DicomType == ""))
                    {
                        if (selectedSS.CanRemoveStructure(tmp)) selectedSS.RemoveStructure(tmp);
                        else
                        {
                            MessageBox.Show(String.Format("Error! \n{0} can't be removed from the structure set!", tmp.Id));
                            return true;
                        }

                        if (!selectedSS.CanAddStructure(itr.Item1, itr.Item2))
                        {
                            MessageBox.Show(String.Format("Error! \n{0} can't be added to the structure set!", itr.Item2));
                            return true;
                        }
                    }
                    else
                    {
                        MessageBox.Show(String.Format("Error! \n{0} is of DICOM type 'None'! \nESAPI can't operate on DICOM type 'None'", itr.Item2));
                        return true;
                    }
                }

                //Need to add the Human body, PTV_BODY, and TS_PTV_VMAT contours manually
                //if these structures were present, they should have been romved (regardless if they were contoured or not). 
                if (itr.Item2.ToLower().Contains("human") || itr.Item2.ToLower().Contains("ptv"))
                {
                    if (selectedSS.CanAddStructure(itr.Item1, itr.Item2))
                    {
                        selectedSS.AddStructure(itr.Item1, itr.Item2);
                        addedStructures.Add(itr.Item2);
                    }
                    else
                    {
                        MessageBox.Show(String.Format("Can't add {0} to the structure set!", itr.Item2));
                        return true;
                    }
                }
            }
            return false;
        }

        private void AddTSStructures(Tuple<string, string> itr1)
        {
            string dicomType = itr1.Item1;
            string structName = itr1.Item2;
            if (selectedSS.CanAddStructure(dicomType, structName))
            {
                selectedSS.AddStructure(dicomType, structName);
                addedStructures.Add(structName);
                optParameters.Add(Tuple.Create(structName, "Mean Dose < Rx Dose"));
            }
            else MessageBox.Show(String.Format("Can't add {0} to the structure set!", structName));
        }
    }
}
