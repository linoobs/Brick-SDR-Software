//=================================================================
// splash.cs
//=================================================================
// PowerSDR is a C# implementation of a Software Defined Radio.
// Copyright (C) 2004-2009  FlexRadio Systems
// Copyright (C) 2010-2019  Doug Wigley
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
//
// You may contact us via email at: sales@flex-radio.com.
// Paper mail may be sent to: 
//    FlexRadio Systems
//    8900 Marybank Dr.
//    Austin, TX 78750
//    USA
//=================================================================
//
//============================================================================================//
// Dual-Licensing Statement (Applies Only to Author's Contributions, Richard Samphire MW0LGE) //
// ------------------------------------------------------------------------------------------ //
// For any code originally written by Richard Samphire MW0LGE, or for any modifications       //
// made by him, the copyright holder for those portions (Richard Samphire) reserves the       //
// right to use, license, and distribute such code under different terms, including           //
// closed-source and proprietary licences, in addition to the GNU General Public License      //
// granted above. Nothing in this statement restricts any rights granted to recipients under  //
// the GNU GPL. Code contributed by others (not Richard Samphire) remains licensed under      //
// its original terms and is not affected by this dual-licensing statement in any way.        //
// Richard Samphire can be reached by email at :  mw0lge@grange-lane.co.uk                    //
//============================================================================================//

using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web;
using System.IO;
using System.Linq;

namespace BrickSDR
{
	public class Splash : System.Windows.Forms.Form
	{
        //MW0LGE
        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

		#region Variable Declarations

		// Threading
		static Splash ms_frmSplash = null;
		static Thread ms_oThread = null;

		// Fade in and out.
		private double m_dblOpacityIncrement = .07;
		private double m_dblOpacityDecrement = .035;
		private const int TIMER_INTERVAL = 50;

		// Status and progress bar
		private string m_sStatus;
		private double m_dblCompletionFraction = 0;
		private Rectangle m_rProgress;

		// Progress smoothing
		private double m_dblLastCompletionFraction = 0.0;
		private double m_dblPBIncrementPerTimerInterval = .015;

		// Self-calibration support
		private bool m_bFirstLaunch = false;
		private DateTime m_dtStart;
		private bool m_bDTSet = false;
		private int m_iIndex = 1;
		private int m_iActualTicks = 0;
		private ArrayList m_alPreviousCompletionFraction;
		private ArrayList m_alActualTimes = new ArrayList();
		private const string REG_KEY_INITIALIZATION = "Initialization";
		private const string REGVALUE_PB_MILISECOND_INCREMENT = "Increment";
		private const string REGVALUE_PB_PERCENTS = "Percents";
		private System.Windows.Forms.Label lblTimeRemaining;
		private System.Windows.Forms.Timer timer1;
		private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSubTitle;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.PictureBox picLogo;
        private System.Windows.Forms.TextBox txtLog;
        private string m_sLastLogged = "";
        private double m_dblSetupProgress = -1;
        private System.Threading.ManualResetEvent m_evtChoice = new System.Threading.ManualResetEvent(false);
        private bool m_bUseBundled = true;
        private Button btnUseBundled;
        private Button btnMeasureAgain;
        private static readonly Color AccentColor = Color.FromArgb(255, 138, 31);
		private System.Windows.Forms.Panel pnlStatus;
        private System.Windows.Forms.Label lblVersion;
        
        private System.ComponentModel.IContainer components;
        #endregion

        private class StartParams
        {
            public string vers;
            public string splash_folder;
        }
		private static StartParams _start_params = new StartParams();

        #region Constructor and Destructor

        public Splash()
		{
            InitializeComponent();
			Common.DoubleBufferAll(this, true);
			this.Opacity = .00;
			timer1.Interval = TIMER_INTERVAL;
			timer1.Start();
			
			this.ShowInTaskbar = false;
		}

		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if(components != null)
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#endregion

		#region Windows Form Designer generated code

		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.picLogo = new System.Windows.Forms.PictureBox();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblSubTitle = new System.Windows.Forms.Label();
            this.lblDescription = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblTimeRemaining = new System.Windows.Forms.Label();
            this.pnlStatus = new System.Windows.Forms.Panel();
            this.lblVersion = new System.Windows.Forms.Label();
            this.txtLog = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.picLogo)).BeginInit();
            this.SuspendLayout();
            //
            // timer1
            //
            this.timer1.Enabled = true;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            //
            // picLogo
            //
            this.picLogo.Image = BrickBranding.Logo;
            this.picLogo.Location = new System.Drawing.Point(24, 22);
            this.picLogo.Name = "picLogo";
            this.picLogo.Size = new System.Drawing.Size(84, 84);
            this.picLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picLogo.TabStop = false;
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 22F, System.Drawing.FontStyle.Bold);
            this.lblTitle.ForeColor = AccentColor;
            this.lblTitle.Location = new System.Drawing.Point(118, 18);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Text = "Brick-SDR";
            //
            // lblSubTitle
            //
            this.lblSubTitle.AutoSize = true;
            this.lblSubTitle.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.lblSubTitle.ForeColor = System.Drawing.Color.FromArgb(170, 176, 190);
            this.lblSubTitle.Location = new System.Drawing.Point(121, 68);
            this.lblSubTitle.Name = "lblSubTitle";
            this.lblSubTitle.Text = "Starting up";
            //
            // lblDescription
            //
            this.lblDescription.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblDescription.ForeColor = System.Drawing.Color.FromArgb(225, 228, 235);
            this.lblDescription.Location = new System.Drawing.Point(24, 120);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(552, 38);
            this.lblDescription.Text = "Brick-SDR is preparing the radio engine and loading the DSP libraries. This only takes a moment.";
            //
            // lblStatus
            //
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(150, 156, 170);
            this.lblStatus.Location = new System.Drawing.Point(24, 168);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(440, 18);
            this.lblStatus.Text = "";
            //
            // lblTimeRemaining
            //
            this.lblTimeRemaining.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblTimeRemaining.ForeColor = System.Drawing.Color.FromArgb(150, 156, 170);
            this.lblTimeRemaining.Location = new System.Drawing.Point(476, 168);
            this.lblTimeRemaining.Name = "lblTimeRemaining";
            this.lblTimeRemaining.Size = new System.Drawing.Size(100, 18);
            this.lblTimeRemaining.Text = "";
            this.lblTimeRemaining.TextAlign = System.Drawing.ContentAlignment.TopRight;
            //
            // pnlStatus
            //
            this.pnlStatus.BackColor = System.Drawing.Color.FromArgb(52, 40, 28);
            this.pnlStatus.Location = new System.Drawing.Point(24, 192);
            this.pnlStatus.Name = "pnlStatus";
            this.pnlStatus.Size = new System.Drawing.Size(552, 10);
            this.pnlStatus.Paint += new System.Windows.Forms.PaintEventHandler(this.pnlStatus_Paint);
            //
            // lblVersion
            //
            this.lblVersion.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblVersion.ForeColor = System.Drawing.Color.FromArgb(150, 156, 170);
            this.lblVersion.Location = new System.Drawing.Point(121, 94);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(455, 18);
            this.lblVersion.Text = "";
            //
            // txtLog
            //
            this.txtLog.BackColor = System.Drawing.Color.Black;
            this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.ForeColor = System.Drawing.Color.FromArgb(215, 218, 224);
            this.txtLog.Location = new System.Drawing.Point(24, 240);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(552, 136);
            this.txtLog.TabStop = false;
            //
            // Splash
            //
            this.BackColor = System.Drawing.Color.FromArgb(22, 24, 33);
            this.ClientSize = new System.Drawing.Size(600, 400);
            this.Controls.Add(this.picLogo);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblSubTitle);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblTimeRemaining);
            this.Controls.Add(this.pnlStatus);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.txtLog);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = BrickBranding.AppIcon;
            this.Name = "Splash";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Brick-SDR";
            this.Load += new System.EventHandler(this.Splash_Load);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.Splash_Paint);
            this.btnUseBundled = makeChoiceButton("Use ready-made file", 24, 270, false);
            this.btnUseBundled.Click += delegate { m_bUseBundled = true; m_evtChoice.Set(); };
            this.btnMeasureAgain = makeChoiceButton("Measure again (recommended, 10-15 min)", 306, 270, true);
            this.btnMeasureAgain.Click += delegate { m_bUseBundled = false; m_evtChoice.Set(); };
            this.Controls.Add(this.btnUseBundled);
            this.Controls.Add(this.btnMeasureAgain);
            ((System.ComponentModel.ISupportInitialize)(this.picLogo)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}
		#endregion
	
		#region Static Methods
		// ************* Static Methods *************** //

        // A static method to create the thread and 
        // launch the SplashScreen.
        static public void ShowSplashScreen(string version, string splash_screen_folder = "")
		{
			// Make sure it is only launched once.
			if( ms_frmSplash != null )
				return;
			ms_oThread = new Thread(ShowForm)
			{
				IsBackground = true,
				Name = "Splash Screen Thread"
			};

			_start_params.vers = version;
			_start_params.splash_folder = splash_screen_folder;

			ms_oThread.Start();
		}

		// A property returning the splash screen instance
		static public Splash SplashForm 
		{
			get
			{
				return ms_frmSplash;
			} 
		}

		// A private entry point for the thread.
		static private void ShowForm()
		{
			ms_frmSplash = new Splash();
			ms_frmSplash.setVersion(_start_params.vers);
			

            Control.CheckForIllegalCrossThreadCalls = false;
			Application.Run(ms_frmSplash);
		}

		// A static method to close the SplashScreen
		static public void CloseForm()
		{
			if( ms_frmSplash != null && ms_frmSplash.IsDisposed == false )
			{
				// Make it start going away.
				ms_frmSplash.m_dblOpacityIncrement = - 
					ms_frmSplash.m_dblOpacityDecrement;
			}
			//Application.Exit();
			ms_oThread = null;  // we do not need these any more.
			ms_frmSplash = null;
		}

        static public void HideForm()
		{
			if(ms_frmSplash != null && ms_frmSplash.IsDisposed == false)
				ms_frmSplash.Hide();
		}

		static public void UnHideForm()
		{
			if(ms_frmSplash != null && ms_frmSplash.IsDisposed == false)
				ms_frmSplash.Show();
		}

        // first start: the FFT wisdom is built, the bar shows the real progress of that work
        static public void SetSetupMode()
        {
            if( ms_frmSplash == null )
                return;
            ms_frmSplash.m_dblSetupProgress = 0;
            ms_frmSplash.lblTitle.Text = "Setting up Brick-SDR";
            ms_frmSplash.lblSubTitle.Text = "First start only";
            ms_frmSplash.lblDescription.Text = "On the first start Brick-SDR measures once how your computer computes the signal processing fastest. This takes about 10-15 minutes.";
        }

        // Shows the two choices for the FFT wisdom and waits for the click.
        // true = use the bundled wisdom file, false = measure again on this computer
        static public bool AskUseBundledWisdom()
        {
            Splash f = ms_frmSplash;
            if( f == null || f.IsDisposed || !f.IsHandleCreated )
                return true;

            f.m_evtChoice.Reset();
            f.BeginInvoke(new MethodInvoker(delegate
            {
                f.lblTitle.Text = "Brick-SDR setup";
                f.lblSubTitle.Text = "First start";
                f.lblDescription.Text = "A ready-made FFT file is included with Brick-SDR. Use it to start right away, or let Brick-SDR measure your computer again (about 10-15 minutes).";
                
                f.btnUseBundled.Visible = true;
                f.btnMeasureAgain.Visible = true;
            }));
            f.m_evtChoice.WaitOne();
            f.BeginInvoke(new MethodInvoker(delegate
            {
                f.btnUseBundled.Visible = false;
                f.btnMeasureAgain.Visible = false;
                
                f.lblTitle.Text = "Brick-SDR";
                f.lblSubTitle.Text = "Starting up";
            }));
            return f.m_bUseBundled;
        }

        private Button makeChoiceButton(string text, int x, int w, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, 200);
            b.Size = new Size(w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            b.FlatAppearance.BorderColor = primary ? AccentColor : Color.FromArgb(90, 96, 112);
            b.BackColor = primary ? AccentColor : Color.FromArgb(34, 37, 50);
            b.ForeColor = primary ? Color.Black : Color.FromArgb(225, 228, 235);
            b.Visible = false;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        static public void SetProgress(double fraction)
        {
            if( ms_frmSplash == null )
                return;
            ms_frmSplash.m_dblSetupProgress = fraction;
        }

        // A static method to set the status and update the reference.
        static public void SetStatus(string newStatus)
		{
			SetStatus(newStatus, true);
		}
    
		// A static method to set the status and optionally update the reference.
		// This is useful if you are in a section of code that has a variable
		// set of status string updates.  In that case, don't set the reference.
		static public void SetStatus(string newStatus, bool setReference)
		{
			if( ms_frmSplash == null )
				return;
			ms_frmSplash.m_sStatus = newStatus;
			if( setReference )
				ms_frmSplash.SetReferenceInternal();
		}

		// Static method called from the initializing application to 
		// give the splash screen reference points.  Not needed if
		// you are using a lot of status strings.
		static public void SetReferencePoint()
		{
			if( ms_frmSplash == null )
				return;
			ms_frmSplash.SetReferenceInternal();

		}

        // ************ Private methods ************
        private void setVersion(string version)
        {
			lblVersion.Text = BasedOnText(version);
        }
        // Internal method for setting reference points.
        private void SetReferenceInternal()
		{
			if( m_bDTSet == false )
			{
				m_bDTSet = true;
				m_dtStart = DateTime.Now;
				ReadIncrements();
			}
			double dblMilliseconds = ElapsedMilliSeconds();
			m_alActualTimes.Add(dblMilliseconds);
			m_dblLastCompletionFraction = m_dblCompletionFraction;
			if( m_alPreviousCompletionFraction != null 
				&& m_iIndex < m_alPreviousCompletionFraction.Count )
				m_dblCompletionFraction = 
					(double)m_alPreviousCompletionFraction[m_iIndex++];
			else
				m_dblCompletionFraction = ( m_iIndex > 0 )? 1: 0;
		}

		// Utility function to return elapsed Milliseconds since the 
		// SplashScreen was launched.
		private double ElapsedMilliSeconds()
		{
			TimeSpan ts = DateTime.Now - m_dtStart;
			return ts.TotalMilliseconds;
		}

		// Function to read the checkpoint intervals 
		// from the previous invocation of the
		// splashscreen from the registry.
		private void ReadIncrements()
		{
			string sPBIncrementPerTimerInterval = 
				RegistryAccess.GetStringRegistryValue( 
				REGVALUE_PB_MILISECOND_INCREMENT, "0.0015");
			double dblResult;

			if( Double.TryParse(sPBIncrementPerTimerInterval, 
				System.Globalization.NumberStyles.Float,
				System.Globalization.NumberFormatInfo.InvariantInfo, 
				out dblResult))
				m_dblPBIncrementPerTimerInterval = dblResult;
			else
				m_dblPBIncrementPerTimerInterval = .0015;

			string sPBPreviousPctComplete = RegistryAccess.GetStringRegistryValue(
				REGVALUE_PB_PERCENTS, "" );

			if( sPBPreviousPctComplete != "" )
			{
				string [] aTimes = sPBPreviousPctComplete.Split(null);
				m_alPreviousCompletionFraction = new ArrayList();

				for(int i = 0; i < aTimes.Length; i++ )
				{
					double dblVal;
					if( Double.TryParse(aTimes[i],
						System.Globalization.NumberStyles.Float, 
						System.Globalization.NumberFormatInfo.InvariantInfo, 
						out dblVal) )
						m_alPreviousCompletionFraction.Add(dblVal);
					else
						m_alPreviousCompletionFraction.Add(1.0);
				}
			}
			else
			{
				m_bFirstLaunch = true;
				lblTimeRemaining.Text = "";
			}      
		}

		// Method to store the intervals (in percent complete)
		// from the current invocation of
		// the splash screen to the registry.
		private void StoreIncrements()
		{
			string sPercent = "";
			double dblElapsedMilliseconds = ElapsedMilliSeconds();
			for( int i = 0; i < m_alActualTimes.Count; i++ )
				sPercent += ((double)m_alActualTimes[i]/
					dblElapsedMilliseconds).ToString("0.####",
					System.Globalization.NumberFormatInfo.InvariantInfo) + " ";

			RegistryAccess.SetStringRegistryValue( 
				REGVALUE_PB_PERCENTS, sPercent );

			m_dblPBIncrementPerTimerInterval = 1.0/(double)m_iActualTicks;
			RegistryAccess.SetStringRegistryValue( 
				REGVALUE_PB_MILISECOND_INCREMENT, 
				m_dblPBIncrementPerTimerInterval.ToString("#.000000",
				System.Globalization.NumberFormatInfo.InvariantInfo));
		}

		#endregion

		#region Event Handlers

		//********* Event Handlers ************

		// Tick Event handler for the Timer control.  
		// Handle fade in and fade out.  Also
		// handle the smoothed progress bar.
		private void timer1_Tick(object sender, System.EventArgs e)
		{
			lblStatus.Text = m_sStatus;

			if( m_sStatus != null && m_sStatus != m_sLastLogged )
			{
				m_sLastLogged = m_sStatus;
				AddLogLine(m_sStatus);
			}

			if( m_dblOpacityIncrement > 0 )
			{
				m_iActualTicks++;
				if( this.Opacity < 1 )
					this.Opacity += m_dblOpacityIncrement;
			}
			else
			{
				if( this.Opacity > 0 )
					this.Opacity += m_dblOpacityIncrement;
				else
                {
                    StoreIncrements();
                    this.Close();

                    //MW0LGE interesting, but removed
                    //// for -autostart option we run a task here and wait for CPU usage < 75%
                    //TimeSpan ttime;
                    //TimeSpan ttimeDiff;
                    //var t = Task.Run(async delegate
                    //{
                    //    Process process = Process.GetCurrentProcess();
                    //    do
                    //    {
                    //        ttime = process.TotalProcessorTime;
                    //        await Task.Delay(500);
                    //        TimeSpan ttime2 = process.TotalProcessorTime;
                    //        ttimeDiff = ttime2.Subtract(ttime);
                    //    } while (ttimeDiff.TotalMilliseconds > 400);
                    //    Console.setPowerOn();
                    //});
                }
            }
            if ( m_dblSetupProgress >= 0 )
            {
                m_rProgress = new Rectangle(0, 0, (int)(pnlStatus.ClientRectangle.Width * m_dblSetupProgress), pnlStatus.ClientRectangle.Height);
                pnlStatus.Invalidate();
                lblTimeRemaining.Text = ((int)(m_dblSetupProgress * 100)).ToString() + " %";
            }
            else if ( m_bFirstLaunch == false && m_dblLastCompletionFraction
				< m_dblCompletionFraction )
			{
				m_dblLastCompletionFraction += m_dblPBIncrementPerTimerInterval;
				int width = (int)Math.Floor(
					pnlStatus.ClientRectangle.Width * m_dblLastCompletionFraction);
				int height = pnlStatus.ClientRectangle.Height;
				int x = pnlStatus.ClientRectangle.X;
				int y = pnlStatus.ClientRectangle.Y;
				if( width > 0 && height > 0 )
				{
					m_rProgress = new Rectangle( x, y, width, height);
                    //MW0LGE pnlStatus.Invalidate(m_rProgress);
                    // this call supresses background draw, which was causing the flicker
                    // annoying that c# invalidate does not have this option.
                    if(pnlStatus!=null && !pnlStatus.IsDisposed)InvalidateRect(pnlStatus.Handle, IntPtr.Zero, false);

					int iSecondsLeft = 1 + (int)(TIMER_INTERVAL * 
						((1.0 - m_dblLastCompletionFraction)/
						m_dblPBIncrementPerTimerInterval)) / 1000;
					if( iSecondsLeft == 1 )
						lblTimeRemaining.Text = string.Format( "1 second");
					else
						lblTimeRemaining.Text = string.Format( "{0} seconds", 
							iSecondsLeft);
				}
			}

		}

		// Paint the portion of the panel invalidated during the tick event.
		private void pnlStatus_Paint(object sender, 
			System.Windows.Forms.PaintEventArgs e)
		{
			if( m_rProgress.Width > 0 && ( m_dblSetupProgress >= 0 || ( m_bFirstLaunch == false && m_iActualTicks > 1 ) ) )
			{
				LinearGradientBrush brBackground = 
					new LinearGradientBrush(m_rProgress, 
					Color.FromArgb(214, 106, 12),
					Color.FromArgb(255, 170, 70), 
					LinearGradientMode.Horizontal);
				e.Graphics.FillRectangle(brBackground, m_rProgress);
			}
		}

		// Close the form if they double click on it.
		private void SplashScreen_DoubleClick(object sender, System.EventArgs e)
		{
			CloseForm();
		}

		#endregion

        private void Splash_Load(object sender, EventArgs e)
        {
			lblStatus.Text = "";
			lblVersion.Text = BasedOnText(_start_params.vers);
			lblTimeRemaining.Text = "";
			AddLogLine("WDSP 2.11 loaded");
            ApplyHoliday();
        }

        // on a holiday of the world, Ukraine or Germany the splash shows a drawn scene for it (BrickHolidays.cs)
        private void ApplyHoliday()
        {
            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHPSDR", "Brick-SDR-x64");
                int advent;
                Holiday h = BrickHolidays.Current(folder, out advent);
                if (h == null) return;
                Bitmap scene = BrickHolidays.Render(h, advent, ClientSize.Width, ClientSize.Height);
                BackgroundImage = scene;
                BackgroundImageLayout = ImageLayout.None;
                foreach (Control c in new Control[] { picLogo, lblTitle, lblSubTitle, lblDescription, lblStatus, lblTimeRemaining, lblVersion }) c.BackColor = Color.Transparent;
                lblStatus.ForeColor = Color.FromArgb(205, 210, 222); lblTimeRemaining.ForeColor = Color.FromArgb(205, 210, 222); lblVersion.ForeColor = Color.FromArgb(205, 210, 222);
                lblSubTitle.ForeColor = Color.FromArgb(215, 220, 232);
                AddLogLine(h.Name);
                txtLog.Visible = false;          // the scene and its greeting are in the place of the log
                Invalidate();
            }
            catch { }
        }

        // "Brick-SDR - based on Thetis 2.10.3.15, WDSP 2.11"
        private static string BasedOnText(string thetisVersion)
        {
            string text = "Brick-SDR v" + BrickBranding.Version;
            if (!string.IsNullOrEmpty(thetisVersion)) text += " - based on Thetis " + thetisVersion + ", WDSP 2.11";
            return text;
        }

        // one line of the start-up log at the bottom of the window
        private void AddLogLine(string text)
        {
            if (txtLog == null || txtLog.IsDisposed) return;
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + "\r\n");
        }

        private void Splash_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(56, 60, 76)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }
    }

    #region Registry Access Class

    /// A class for managing registry access.
    public class RegistryAccess
	{
		private const string SOFTWARE_KEY = "Software";
		private const string COMPANY_NAME = "OpenHPSDR";
		private const string APPLICATION_NAME = "Brick-SDR";

		// Method for retrieving a Registry Value.
		static public string GetStringRegistryValue(string key, 
			string defaultValue)
		{
			RegistryKey rkCompany;
			RegistryKey rkApplication;

			rkCompany = Registry.CurrentUser.OpenSubKey(SOFTWARE_KEY, 
				false).OpenSubKey(COMPANY_NAME, false);
			if( rkCompany != null )
			{
				rkApplication = rkCompany.OpenSubKey(APPLICATION_NAME, true);
				if( rkApplication != null )
				{
					foreach(string sKey in rkApplication.GetValueNames())
					{
						if( sKey == key )
						{
							return (string)rkApplication.GetValue(sKey);
						}
					}
				}
			}
			return defaultValue;
		}

		// Method for storing a Registry Value.
		static public void SetStringRegistryValue(string key, 
			string stringValue)
		{
			RegistryKey rkSoftware;
			RegistryKey rkCompany;
			RegistryKey rkApplication;

			rkSoftware = Registry.CurrentUser.OpenSubKey(SOFTWARE_KEY, true);
			rkCompany = rkSoftware.CreateSubKey(COMPANY_NAME);
			if( rkCompany != null )
			{
				rkApplication = rkCompany.CreateSubKey(APPLICATION_NAME);
				if( rkApplication != null )
				{
					rkApplication.SetValue(key, stringValue);
				}
			}
		}
	}

	#endregion
}
