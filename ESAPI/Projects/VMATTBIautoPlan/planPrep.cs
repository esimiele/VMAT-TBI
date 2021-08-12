using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace VMATTBIautoPlan
{
    class planPrep
    {
        //common variables
        ExternalPlanSetup vmatPlan = null;
        IEnumerable<ExternalPlanSetup> appaPlan = new List<ExternalPlanSetup> { };
        int numVMATIsos = 0;
        int numIsos;
        //empty vectors to hold the z positions of each isocenter and the names of each isocenter
        List<double> zPositions = new List<double> { };
        //empty vectors to hold the isocenter position of one beam from each isocenter and the names of each isocenter
        List<Tuple<double, double, double>> isoPositions = new List<Tuple<double, double, double>> { };
        List<string> names = new List<string> { };
        List<List<Beam>> vmatBeamsPerIso = new List<List<Beam>> { };
        List<List<Beam>> appaBeamsPerIso = new List<List<Beam>> { };
        bool legsSeparated = false;
        List<ExternalPlanSetup> separatedPlans = new List<ExternalPlanSetup> { };
        public bool flashRemoved = false;

        public planPrep(ExternalPlanSetup vmat, IEnumerable<ExternalPlanSetup> appa)
        {
            //copy arguments into local variables
            vmatPlan = vmat;
            appaPlan = appa;
            //if there is more than one AP/PA legs plan in the list, this indicates that the user already separated these plans. Don't separate them in this script
            if (appa.Count() > 1) legsSeparated = true;
        }

        public bool getShiftNote()
        {
            //loop through each beam in the vmat plan, grab the isocenter position of the beam. Compare the z position of each isocenter to the list of z positions in the vector. 
            //If no match is found, this is a new isocenter. Add it to the stack. If it is not unique, this beam belongs to an existing isocenter group --> ignore it
            //also grab instances of each beam in each isocenter and save them (used for separating the plans later)
            string beamFormatMessage = "The following beams are not in the correct format:" + Environment.NewLine;
            bool beamFormatError = false;
            foreach (Beam b in vmatPlan.Beams.Where(x => !x.IsSetupField))
            {
                if(! int.TryParse(b.Id.Substring(0, 2).ToString(), out int dummy))
                {
                    beamFormatMessage += b.Id + Environment.NewLine;
                    if (!beamFormatError) beamFormatError = true;
                }
            }
            foreach (ExternalPlanSetup p in appaPlan)
            {
                foreach (Beam b in p.Beams.Where(x => !x.IsSetupField))
                {
                    if (!int.TryParse(b.Id.Substring(0, 2).ToString(), out int dummy))
                    {
                        beamFormatMessage += b.Id + Environment.NewLine;
                        if(!beamFormatError) beamFormatError = true;
                    }
                }
            }
            if (beamFormatError)
            {
                beamFormatMessage += Environment.NewLine + "Make sure there is a space after the beam number! Please fix and try again!";
                MessageBox.Show(beamFormatMessage);
                return true;
            }

            List<Beam> beams = new List<Beam> { };
            foreach (Beam b in vmatPlan.Beams.Where(x => !x.IsSetupField).OrderBy(o => int.Parse(o.Id.Substring(0,2).ToString())))
            {
                VVector v = b.IsocenterPosition;
                v = vmatPlan.StructureSet.Image.DicomToUser(v, vmatPlan);
                IEnumerable<Tuple<double,double,double>> d = isoPositions.Where(k => k.Item3 == v.z);
                if (!d.Any())
                {
                    isoPositions.Add(Tuple.Create(v.x, v.y, v.z));
                    numVMATIsos++;
                    //do NOT add the first detected isocenter to the number of beams per isocenter list. Start with the second isocenter 
                    //(otherwise there will be no beams in the first isocenter, the beams in the first isocenter will be attached to the second isocenter, etc.)
                    if (numVMATIsos > 1)
                    {
                        //NOTE: it is important to have 'new List<Beam>(beams)' as the argument rather than 'beams'. A list of a list is essentially a pointer to a list, so if you delete the sublists,
                        //the list of lists will have the correct number of elements, but they will all be empty
                        vmatBeamsPerIso.Add(new List<Beam>(beams));
                        beams.Clear();
                    }
                }
                //add the current beam to the sublist
                beams.Add(b);
            }
            //add the beams from the last isocenter to the vmat beams per iso list
            vmatBeamsPerIso.Add(new List<Beam>(beams));
            beams.Clear();
            

            //copy number of vmat isocenters determined above onto the total number of isos
            numIsos = numVMATIsos;
            //if the ap/pa plan is NOT null, then get the isocenter position(s) of those beams as well. Do the same thing as above
            foreach(ExternalPlanSetup p in appaPlan)
            {
                foreach (Beam b in p.Beams.Where(x => !x.IsSetupField).OrderBy(o => int.Parse(o.Id.Substring(0, 2).ToString())))
                {
                    VVector v = b.IsocenterPosition;
                    v = p.StructureSet.Image.DicomToUser(v, p);
                    IEnumerable<Tuple<double, double, double>> d = isoPositions.Where(k => k.Item3 == v.z);
                    if (!d.Any())
                    {
                        //zPositions.Add(v.z);
                        isoPositions.Add(Tuple.Create(v.x, v.y, v.z));
                        numIsos++;
                        if(numIsos - numVMATIsos > 1)
                        {
                            //same as above
                            appaBeamsPerIso.Add(new List<Beam>(beams));
                            beams.Clear();
                        }
                    }
                    beams.Add(b);
                }
                appaBeamsPerIso.Add(new List<Beam>(beams));
            }

            //logic to assign the isocenter names based on the number of vmat isos and the total number of isos (taken and modified from the generateTS class)
            names.Add("Head");
            if (numVMATIsos == numIsos)
            {
                if (numVMATIsos == 2) names.Add("Pelvis");
                else
                {
                    names.Add("Pelvis");
                    names.Add("Legs");
                }
            }
            else
            {
                if (numVMATIsos == 2) names.Add("Pelvis");
                else
                {
                    names.Add("Chest");
                    names.Add("Pelvis");
                }
                names.Add("AP / PA upper legs");
                if (numIsos - numVMATIsos == 2) names.Add("AP / PA lower legs");
                //greater than 2 AP/pA isos (it happened once...)
                else if (numIsos - numVMATIsos > 2) { names.Add("AP / PA Mid legs"); names.Add("AP / PA lower legs"); }
            }

            //get the user origin in user coordinates
            VVector uOrigin = vmatPlan.StructureSet.Image.UserOrigin;
            uOrigin = vmatPlan.StructureSet.Image.DicomToUser(uOrigin, vmatPlan);
            //vector to hold the isocenter name, the x,y,z shifts from CT ref, and the shifts between each adjacent iso for each axis (LR, AntPost, SupInf)
            List<Tuple<string, Tuple<double, double, double>, Tuple<double, double, double>>> shifts = new List<Tuple<string, Tuple<double, double, double>, Tuple<double, double, double>>> { };
            double SupInfShifts = 0.0;
            double AntPostShifts = 0.0;
            double LRShifts = 0.0;
            int count = 0;
            foreach (Tuple<double, double, double> pos in isoPositions)
            {
                //each zPosition inherently represents the shift from CT ref in User coordinates
                Tuple<double,double,double> CTrefShifts = Tuple.Create(pos.Item1 / 10, pos.Item2 / 10 , pos.Item3 / 10);
                //copy shift from CT ref to sup-inf shifts for first element, otherwise calculate the separation between the current and previous iso (from sup to inf direction)
                //calculate the relative shifts between isocenters (the first isocenter is the CTrefShift)
                if (count == 0)
                {
                    SupInfShifts = (isoPositions.ElementAt(count).Item3 / 10);
                    AntPostShifts = (isoPositions.ElementAt(count).Item2 / 10);
                    LRShifts = (isoPositions.ElementAt(count).Item1 / 10);
                }
                else
                {
                    SupInfShifts = (isoPositions.ElementAt(count).Item3 - isoPositions.ElementAt(count - 1).Item3) / 10;
                    AntPostShifts = (isoPositions.ElementAt(count).Item2 - isoPositions.ElementAt(count - 1).Item2) / 10;
                    LRShifts = (isoPositions.ElementAt(count).Item1 - isoPositions.ElementAt(count - 1).Item1) / 10;
                }
                //add the iso name, CT ref shift, and sup-inf shift to the vector
                shifts.Add(Tuple.Create(names.ElementAt(count), CTrefShifts, Tuple.Create(LRShifts, AntPostShifts, SupInfShifts)));
                count++;
            }

            //convert the user origin back to dicom coordinates
            uOrigin = vmatPlan.StructureSet.Image.UserToDicom(uOrigin, vmatPlan);

            //grab the couch surface
            Structure couchSurface = vmatPlan.StructureSet.Structures.FirstOrDefault(x => x.Id.ToLower() == "couchsurface");
            double TT = 0;
            //check if couch is present. Warn if not found, otherwise it is the separation between the the beam isocenter position and the minimum y-position of the couch surface (in dicom coordinates)
            if (couchSurface == null) MessageBox.Show("Warning! No couch surface structure found!");
            else TT = (vmatPlan.Beams.First(x => !x.IsSetupField).IsocenterPosition.y - couchSurface.MeshGeometry.Positions.Min(p => p.Y)) / 10;

            //create the message
            string message = "";
            if (couchSurface != null) message += "***Bars out***\r\n";
            else message += "No couch surface structure found in plan!\r\n";
            //check if AP/PA plans are in FFS orientation
            if (appaPlan.Any() && appaPlan.Where(x => x.TreatmentOrientation != PatientOrientation.FeetFirstSupine).Any())
            {
                message += "The following AP/PA plans are NOT in the FFS orientation:\r\n";
                foreach (ExternalPlanSetup p in appaPlan) if (p.TreatmentOrientation != PatientOrientation.FeetFirstSupine) message += p.Id + "\r\n";
                message += "WARNING! THE COUCH SHIFTS FOR THESE PLANS WILL NOT BE ACCURATE!\r\n";
            }
            if (numIsos > numVMATIsos) message += "VMAT TBI setup per procedure. Please ensure the matchline on Spinning Manny and the bag matches\r\n";
            else message += "VMAT TBI setup per procedure. No Spinning Manny.\r\r\n";
            message += String.Format("TT = {0:0.0} cm for all plans\r\n", TT);
            message += "Dosimetric shifts SUP to INF:\r\n";

            //write the first set of shifts from CT ref before the loop. 12-23-2020 support added for the case where the lat/vert shifts are non-zero
            if (Math.Abs(shifts.ElementAt(0).Item3.Item1) >= 0.1 || Math.Abs(shifts.ElementAt(0).Item3.Item2) >= 0.1)
            {
                message += String.Format("{0} iso shift from CT REF:", shifts.ElementAt(0).Item1) + System.Environment.NewLine;
                if (Math.Abs(shifts.ElementAt(0).Item3.Item1) >= 0.1) message += String.Format("X = {0:0.0} cm {1}", Math.Abs(shifts.ElementAt(0).Item3.Item1), (shifts.ElementAt(0).Item3.Item1) > 0 ? "LEFT" : "RIGHT") + System.Environment.NewLine;
                if (Math.Abs(shifts.ElementAt(0).Item3.Item2) >= 0.1) message += String.Format("Y = {0:0.0} cm {1}", Math.Abs(shifts.ElementAt(0).Item3.Item2), (shifts.ElementAt(0).Item3.Item2) > 0 ? "POST" : "ANT") + System.Environment.NewLine;
                message += String.Format("Z = {0:0.0} cm {1}", shifts.ElementAt(0).Item3.Item3, Math.Abs(shifts.ElementAt(0).Item3.Item3) > 0 ? "SUP" : "INF") + System.Environment.NewLine;
            }
            else message += String.Format("{0} iso shift from CT ref = {1:0.0} cm {2} ({3:0.0} cm {4} from CT ref)\r\n", shifts.ElementAt(0).Item1, Math.Abs(shifts.ElementAt(0).Item3.Item3), shifts.ElementAt(0).Item3.Item3 > 0 ? "SUP" : "INF", Math.Abs(shifts.ElementAt(0).Item2.Item3), shifts.ElementAt(0).Item2.Item3 > 0 ? "SUP" : "INF");

            for (int i = 1; i < numIsos; i++)
            {
                if (i == numVMATIsos)
                {
                    //if numVMATisos == numIsos this message won't be displayed. Otherwise, we have exhausted the vmat isos and need to add these lines to the shift note
                    message += "Rotate Spinning Manny, shift to opposite Couch Lat\r\n";
                    message += "Upper Leg iso - same Couch Lng as Pelvis iso\r\n";
                    //let the therapists know that they need to shift couch lateral to the opposite side if the initial lat shift was non-zero
                    if (Math.Abs(shifts.ElementAt(0).Item3.Item1) >= 0.1) message += "Shift couch lateral to opposite side!\r\n";
                }
                //shift messages when the current isocenter is NOT the number of vmat isocenters (i.e., the first ap/pa isocenter). First case is for the vmat isocenters, the second case is when the isocenters are ap/pa (but not the first ap/pa isocenter)
                else if (i < numVMATIsos) message += String.Format("{0} iso shift from {1} iso = {2:0.0} cm {3} ({4:0.0} cm {5} from CT ref)\r\n", shifts.ElementAt(i).Item1, shifts.ElementAt(i - 1).Item1, Math.Abs(shifts.ElementAt(i).Item3.Item3), shifts.ElementAt(i).Item3.Item3 > 0 ? "SUP" : "INF", Math.Abs(shifts.ElementAt(i).Item2.Item3), shifts.ElementAt(i).Item2.Item3 > 0 ? "SUP" : "INF");
                else message += String.Format("{0} iso shift from {1} iso = {2:0.0} cm {3} ({4:0.0} cm {5} from CT ref)\r\n", shifts.ElementAt(i).Item1, shifts.ElementAt(i - 1).Item1, Math.Abs(shifts.ElementAt(i).Item3.Item3), shifts.ElementAt(i).Item3.Item3 > 0 ? "INF" : "SUP", Math.Abs(shifts.ElementAt(i).Item2.Item3), shifts.ElementAt(i).Item2.Item3 > 0 ? "INF" : "SUP");
            }

            //copy to clipboard and inform the user it's done
            Clipboard.SetText(message);
            MessageBox.Show("Shifts have been copied to the clipboard! \r\nPaste them into the journal note!");
            return false;
        }

        public bool separate()
        {
            //check for setup fields in the vmat and AP/PA plans
            if(!vmatPlan.Beams.Where(x => x.IsSetupField).Any() || (appaPlan.Count() > 0 && !legsSeparated && !appaPlan.First().Beams.Where(x => x.IsSetupField).Any()))
            {
                string problemPlan = "";
                if (!vmatPlan.Beams.Where(x => x.IsSetupField).Any()) problemPlan = "VMAT plan";
                else problemPlan = "AP/PA plan(s)";
                confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                CUI.message.Text = String.Format("I didn't find any setup fields in the {0}.", problemPlan) + Environment.NewLine + Environment.NewLine + "Are you sure you want to continue?!";
                CUI.ShowDialog();
                if (!CUI.confirm) return true;
            }

            //check if flash was used in the plan. If so, ask the user if they want to remove these structures as part of cleanup
            if (checkForFlash())
            {
                confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                CUI.message.Text = "I found some structures in the structure set for generating flash." + Environment.NewLine + Environment.NewLine + "Do you want me to remove them?!";
                CUI.button1.Text = "No";
                CUI.ShowDialog();
                if (CUI.confirm) if(removeFlashStr()) return true;
            }
            //counter for indexing names
            int count = 0;
            //loop through the list of beams in each isocenter
            foreach(List<Beam> beams in vmatBeamsPerIso)
            {
                //string message = "";
                //foreach (Beam b in beams) message += b.Id + "\n";
                //MessageBox.Show(message);

                //copy the plan, set the plan id based on the counter, and make a empty list to hold the beams that need to be removed
                ExternalPlanSetup newplan = (ExternalPlanSetup)vmatPlan.Course.CopyPlanSetup(vmatPlan);
                newplan.Id = String.Format("{0} {1}", count + 1, names.ElementAt(count));
                List<Beam> removeMe = new List<Beam> { };
                //can't add reference point to plan because it must be open in Eclipse for ESAPI to perform this function. Need to fix in v16
                //newplan.AddReferencePoint(newplan.StructureSet.Structures.First(x => x.Id.ToLower() == "ptv_body"), null, newplan.Id, newplan.Id);
                //add the current plan copy to the separatedPlans list
                separatedPlans.Add(newplan);
                //loop through each beam in the plan copy and compare it to the list of beams in the current isocenter
                foreach(Beam b in newplan.Beams)
                {
                    //if the current beam in newPlan is NOT found in the beams list, add it to the removeMe list. This logic has to be applied. You can't directly remove the beams in this loop as ESAPI will
                    //complain that the enumerable that it is using to index the loop changes on each iteration (i.e., newplan.Beams changes with each iteration). Do NOT add setup beams to the removeMe list. The
                    //idea is to have dosi add one set of setup fields to the original plan and then not remove those for each created plan. Unfortunately, dosi will have to manually adjust the iso position for
                    //the setup fields in each new plan (no way to adjust the existing isocenter of an existing beam, it has to be re-added)
                    if (!beams.Where(x => x.Id == b.Id).Any() && !b.IsSetupField) removeMe.Add(b);
                }
                //now remove the beams for the current plan copy
                foreach (Beam b in removeMe) newplan.RemoveBeam(b);
                count++;
            }

            //do the same as above, but for the AP/PA legs plan
            if (!legsSeparated)
            {
                foreach (List<Beam> beams in appaBeamsPerIso)
                {
                    //string message = "";
                    //foreach (Beam b in beams) message += b.Id + "\n";
                    //MessageBox.Show(message);
                    ExternalPlanSetup newplan = (ExternalPlanSetup)appaPlan.First().Course.CopyPlanSetup(appaPlan.First());
                    List<Beam> removeMe = new List<Beam> { };
                    newplan.Id = String.Format("{0} {1}", count + 1, (names.ElementAt(count).Contains("upper") ? "Upper Legs" : "Lower Legs"));
                    //newplan.AddReferencePoint(newplan.StructureSet.Structures.First(x => x.Id.ToLower() == "ptv_body"), null, newplan.Id, newplan.Id);
                    separatedPlans.Add(newplan);
                    foreach (Beam b in newplan.Beams)
                    {
                        //if the current beam in newPlan is NOT found in the beams list, then remove it from the current new plan
                        if (!beams.Where(x => x.Id == b.Id).Any() && !b.IsSetupField) removeMe.Add(b);
                    }
                    foreach (Beam b in removeMe) newplan.RemoveBeam(b);
                    count++;
                }
            }
            //inform the user it's done
            string message = "Original plan(s) have been separated! \r\nBe sure to set the target volume and primary reference point!\r\n";
            if (vmatPlan.Beams.Where(x => x.IsSetupField).Any() || (appaPlan.Count() > 0 && !legsSeparated && appaPlan.First().Beams.Where(x => x.IsSetupField).Any()))
                message += "Also reset the isocenter position of the setup fields!";
            MessageBox.Show(message);
            return false;
        }

        private bool checkForFlash()
        {
            //look in the structure set to see if any of the structures contain the string 'flash'. If so, return true indicating flash was included in this plan
            IEnumerable<Structure> flashStr = vmatPlan.StructureSet.Structures.Where(x => x.Id.ToLower().Contains("flash"));
            if(flashStr.Any()) foreach (Structure s in flashStr) if (!s.IsEmpty) return true;
            return false;
        }

        private bool removeFlashStr()
        {
            //remove the structures used to generate flash in the plan
            StructureSet ss = vmatPlan.StructureSet;
            //check to see if this structure set is used in any other calculated plans that are NOT the _VMAT TBI plan or any of the AP/PA legs plans
            string message = "";
            List<ExternalPlanSetup> otherPlans = new List<ExternalPlanSetup> { };
            foreach (Course c in vmatPlan.Course.Patient.Courses)
            {
                foreach (ExternalPlanSetup p in c.ExternalPlanSetups)
                {
                    if ((p != vmatPlan && !appaPlan.Where(x => x == p).Any()) && p.IsDoseValid && p.StructureSet == ss)
                    {
                        message += String.Format("Course: {0}, Plan: {1}", c.Id, p.Id) + System.Environment.NewLine;
                        otherPlans.Add(p);
                    }
                }
            }
            //photon dose calculation model type
            string calcModel = vmatPlan.GetCalculationModel(CalculationType.PhotonVolumeDose);
            if (otherPlans.Count > 0)
            {
                //if some plans were found that use this structure set and have dose calculated, inform the user and ask if they want to continue WITHOUT removing flash.
                message = "The following plans have dose calculated and use the same structure set:" + System.Environment.NewLine + message + System.Environment.NewLine;
                message += "I need to remove the calculated dose from these plans before removing the flash structures." + System.Environment.NewLine;
                message += "Continue?";
                confirmUI CUI = new VMATTBIautoPlan.confirmUI();
                CUI.message.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
                CUI.message.Text = message;
                CUI.ShowDialog();
                //need to return from this function regardless of what the user decides
                if (!CUI.confirm) return true;
                foreach (ExternalPlanSetup p in otherPlans)
                {
                    p.ClearCalculationModel(CalculationType.PhotonVolumeDose);
                    p.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
                }
            }
            //dumbass way around the issue of modifying structures in a plan that already has dose calculated... reset the calculation model, make the changes you need, then reset the calculation model
            vmatPlan.ClearCalculationModel(CalculationType.PhotonVolumeDose);
            vmatPlan.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
            foreach (ExternalPlanSetup p in appaPlan)
            {
                p.ClearCalculationModel(CalculationType.PhotonVolumeDose);
                p.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
            }
            IEnumerable <Structure> flashStr = ss.Structures.Where(x => x.Id.ToLower().Contains("flash"));
            List<Structure> removeMe = new List<Structure> { };
            //can't remove directly from flashStr because the vector size would change on each loop iteration
            foreach (Structure s in flashStr) if (!s.IsEmpty) if(ss.CanRemoveStructure(s)) removeMe.Add(s);
            foreach (Structure s in removeMe) ss.RemoveStructure(s);
            
            //from the generateTS class, the human_body structure was a copy of the body structure BEFORE flash was added. Therefore, if this structure still exists, we can just copy it back onto the body
            Structure bodyCopy = ss.Structures.FirstOrDefault(x => x.Id.ToLower() == "human_body");
            if (bodyCopy != null && !bodyCopy.IsEmpty)
            {
                Structure body = ss.Structures.First(x => x.Id.ToLower() == "body");
                body.SegmentVolume = bodyCopy.Margin(0.0);
                if (ss.CanRemoveStructure(bodyCopy)) ss.RemoveStructure(bodyCopy);
            }
            else MessageBox.Show("WARNING 'HUMAN_BODY' STRUCTURE NOT FOUND! BE SURE TO RE-CONTOUR THE BODY STRUCTURE!");
            flashRemoved = true;
            
            return false;
        }

        public void calculateDose()
        {
            //loop through each plan in the separatedPlans list (generated in the separate method above) and calculate dose for each plan
            foreach(ExternalPlanSetup p in separatedPlans) p.CalculateDose();
        }
    }
}
