using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VMATTBIautoPlan
{
    public partial class enterMissingInfo : Form
    {
        public bool confirm = false;
        public enterMissingInfo()
        {
            InitializeComponent();
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void confirm_Click(object sender, EventArgs e)
        {
            confirm = true;
            this.Close();
        }
    }
}

