using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VMATTBIautoPlan
{
    class isoNameHelper
    {
        public List<string> getIsoNames(int numVMATIsos, int numIsos)
        {
            List<string> isoNames = new List<string> { };

            isoNames.Add("Head");
            if (numIsos > numVMATIsos)
            {
                if (numVMATIsos == 2) isoNames.Add("Pelvis");
                else
                {
                    isoNames.Add("Chest");
                    if (numVMATIsos == 3) isoNames.Add("Pelvis");
                    else if (numVMATIsos == 4) { isoNames.Add("Abdomen"); isoNames.Add("Pelvis"); }
                }
                isoNames.Add("AP / PA upper legs");
                if (numIsos == numVMATIsos + 2) isoNames.Add("AP / PA lower legs");
            }
            else
            {
                if (numVMATIsos == 2) isoNames.Add("Pelvis");
                else
                {
                    isoNames.Add("Chest");
                    if (numVMATIsos == 3) isoNames.Add("Legs");
                    else if (numVMATIsos == 4) { isoNames.Add("Pelvis"); isoNames.Add("Legs"); }
                    else if (numVMATIsos == 5) { isoNames.Add("Abdomen"); isoNames.Add("Pelvis"); isoNames.Add("Legs"); }
                    else if (numVMATIsos == 6) { isoNames.Add("Abdomen"); isoNames.Add("Pelvis"); isoNames.Add("Upper Legs"); isoNames.Add("Lower Legs"); }
                }
            }
            return isoNames;
        }
    }
}
