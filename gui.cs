using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Reflection;

using SeeSharpTools.JY.DSP.SoundVibration;
using SeeSharpTools.JXI.SignalProcessing.Window;
using SeeSharpTools.JXI.SignalProcessing.SpectrumAnalysis.RFSASpectrum;
using SeeSharpTools.JXI.SignalProcessing.GeneralSpectrum;



namespace exe_demo
{
    public partial class MainForm : Form
    {
        private string filename;
        private string last_flle_path;
                
        public MainForm()
        {
            InitializeComponent();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
        private const int WM_VSCROLL = 277;
        private const int SB_PAGEBOTTOM = 7;

        public static void ScrollToBottom(RichTextBox MyRichTextBox)
        {
            SendMessage(MyRichTextBox.Handle, WM_VSCROLL, (IntPtr)SB_PAGEBOTTOM, IntPtr.Zero);
        }

        private void showLog(string log)
        {

            if (richTextBoxLog.IsHandleCreated == false) return;

            this.Invoke((EventHandler)(delegate
            {
                richTextBoxLog.SelectionColor = Color.Black;
                richTextBoxLog.AppendText(log + "\r\n");
                ScrollToBottom(richTextBoxLog); // richTextBoxLog.ScrollToCaret();

            }));
        }

        public static Mutex mutex = new Mutex();

        // 事件委托（先声明委托类型，再声明事件是委托）（打开按钮的点击事件）的事件处理程序
        // 此处buttonOpenFile.Click为内置事件，无需事先定义
        private void buttonOpenFile_Click(object sender, EventArgs e)
        {

            OpenFileDialog openFileDialog = new OpenFileDialog();

            if (last_flle_path == null || System.IO.Directory.Exists(last_flle_path) == false)
            {

                openFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(Application.ExecutablePath);//注意这里写路径时要用c://而不是c:/                
            }
            else
            {

                openFileDialog.InitialDirectory = last_flle_path;//注意这里写路径时要用c://而不是c:/
            }


            //openFileDialog.Filter = "文本文件|*.*|C#文件|*.cs|所有文件|*.*";
            openFileDialog.RestoreDirectory = true;
            openFileDialog.FilterIndex = 1;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                filename = openFileDialog.FileName;
                textBox_fileName.Text = filename;
                showLog("File name:" + filename + "\n");
                showLog("File size:" + new System.IO.FileInfo(filename).Length + "\n");

                last_flle_path = new System.IO.FileInfo(filename).DirectoryName;
                // showLog("openFileDialog:" + last_flle_path);
            }

        }

        struct PACK
        {
            public UInt32 len;
            public UInt32 d_ty;
            public UInt32 t0_id;
            public UInt32 tg_id;
            public UInt64 tof;
            public double rsv;
            public UInt32 ch_id;
            public UInt16[] data;
        }

        List<PACK> packs = new List<PACK>();

        private void unpackdata()
        {
            bool nonstop;

            byte[] readdata;

            FileStream F = new FileStream(filename, FileMode.Open, FileAccess.Read);

            BinaryReader G = new BinaryReader(F);

            int n = 0, k, p;

            UInt32 l;

            while (true)
            {
                nonstop = true;
                while (nonstop)
                {
                    try
                    {
                        // l仅仅是用来检查数据帧头是否出错的，没有绘图的实际意义
                        l = G.ReadUInt32();          
                    }
                    catch
                    {
                        if (n == 0)
                        {
                            showLog("no pack is found in" + filename);
                        }
                        F.Close();
                        return;
                    }

                    if (l != 0x000090eb)
                    {
                        showLog("pack head is not found, skip four bytes");
                    }
                    else nonstop = false;
                }
                // 取每个pack的长度字段（在pack首部4B）
                readdata = G.ReadBytes(4);

                Array.Reverse(readdata);

                UInt32 tmplen;

                tmplen = BitConverter.ToUInt32(readdata, 0);

                n++;

                PACK pACK = new PACK();



                pACK.len = tmplen;

                byte[] temp;
                // 取每个pack的剩余字段（在pack首部后40B）
                temp = G.ReadBytes(40);
                // 网络发送为大端字节序->转成小端字节序（主机字节序；字节间序同，只需转各字节内存储序），且读的所有数据项都是4B的倍数，每次读4B的倍数即可
                Array.Reverse(temp);





                pACK.ch_id = BitConverter.ToUInt32(temp, 0);

                //pACK.rsv = BitConverter.ToUInt32(temp, 4);

                pACK.tof = BitConverter.ToUInt64(temp, 20);

                pACK.tg_id = BitConverter.ToUInt32(temp, 28);

                pACK.t0_id = BitConverter.ToUInt32(temp, 32);

                pACK.d_ty = BitConverter.ToUInt32(temp, 36);

                // 表示总共需要读取的字节数k（总长-非数据部分）
                k = (int)tmplen - 48;   
                // data数组元素大小修改为2B
                pACK.data = new UInt16[k / 2];

                for (p = 0; p < k / 2; p++)
                {
                    readdata = G.ReadBytes(2);

                    Array.Reverse(readdata);

                    pACK.data[p] = BitConverter.ToUInt16(readdata, 0);
                }


                packs.Add(pACK);

            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            unpackdata();

            int len = packs[0].data.Length;

            int i, j;

            double[] packed = new double[len];

            double[] ave = new double[30];                //ave是平均值

            double[][] unpacked = new double[30][];

            double[] ENOB = new double[30], SINAD = new double[30], SNR = new double[30], THD = new double[30];

            double finalENOB = 0, finalSINAD = 0, finalSNR = 0, finalTHD = 0;

            for (j = 0; j < 30; j++)
            {
                double[] test = new double[len];
                for (i = 0; i < len; i++)
                {
                    test[i] = (double)packs[j].data[i];
                    ave[j] = ave[j] + test[i];
                    //unpacked[i] = (unpacked[i] - 2048) / 2048;
                }
                ave[j] = ave[j] / len;
                unpacked[j] = test;
                for (i = 0; i < len; i++)
                {
                    packed[i] = test[i] - ave[j];
                }
                ToneAnalysisResult toneAnalysisResult = HarmonicAnalyzer.ToneAnalysis(packed, 1, 10, true);
                ENOB[j] = toneAnalysisResult.ENOB;
                finalENOB = finalENOB + ENOB[j];

                SINAD[j] = toneAnalysisResult.SINAD;
                finalSINAD = finalSINAD + SINAD[j];

                SNR[j] = toneAnalysisResult.SNR;
                finalSNR = finalSNR + SNR[j];

                THD[j] = toneAnalysisResult.THD;
                finalTHD = finalTHD + THD[j];
            }
            finalENOB = finalENOB / 30;
            finalSINAD = finalSINAD / 30;
            finalSNR = finalSNR / 30;
            finalTHD = finalTHD / 30;


            label2.Text = "ENOB:" + finalENOB.ToString("F2");
            label3.Text = "SINAD:" + finalSINAD.ToString("F2");
            label4.Text = "SNR:" + finalSNR.ToString("F2");
            label5.Text = "THD:" + (-finalTHD).ToString("F2");

            /*
            label2.Text = "ENOB:11.24";
            label3.Text = "SINAD:68.88";
            label4.Text = "SNR:69.34";
            label5.Text = "THD:78.81";
            */

            Console.WriteLine(finalENOB);
            Console.WriteLine(finalSINAD);
            Console.WriteLine(finalSNR);
            Console.WriteLine(-finalTHD);


            easyChart_readData1.Plot(unpacked[0]);



            /*byte[] readData = new byte[64];                                 //声明8位无符号整型数组

            FileStream F = new FileStream(filename, FileMode.Open, FileAccess.Read);

            int[] adc_data = new int[25000];                      //声明整型数组

            int i, index = 1;                                 //声明用于计数的变量

            int j = 0;                                           //j表示文件读取的位置

            for (i = 0; i < 25000; i++)
            {
                adc_data[i] = 0;
            }

            List<int> listADC = new List<int> { };                //声明一个集合，用于合并数组

            int l;                //读取的总字节数

            F.Position = 0;

            while (true)
            {
                F.Position = j;
                l = F.Read(readData, 0, 64);
                j = j + 64;

                if (l < 64)
                {
                    showLog("\n end of file\n");
                    break;
                }

                int adc_code1, adc_code2;

                if ((readData[0] >> 5) == 4)
                {
                    showLog("frame start ");
                }

                else if ((readData[0] >> 5) == 6)
                {
                    showLog("frame end_"+ index);

                    index = index + 1;
                }

                else
                {
                    for (i = 3; i < 33; i = i + 3)
                    {
                        adc_code1 = (readData[i - 1] << 4) + ((readData[i] & 0xF0) >> 4);

                        adc_code2 = ((readData[i] & 0x0F) << 8) + readData[i + 1];

                        //  adc_data=水平拼接adc-code1,adc-code2
                        listADC.Add(adc_code1);
                        listADC.Add(adc_code2);
                    }

                    for (i = 35; i < 64; i = i + 3)
                    {
                        adc_code1 = (readData[i - 1] << 4) + ((readData[i] & 0xF0) >> 4);

                        adc_code2 = ((readData[i] & 0x0F) << 8) + readData[i + 1];

                        listADC.Add(adc_code1);
                        listADC.Add(adc_code2);
                    }

                }
            }
            F.Close();
            adc_data = listADC.ToArray();

            double[] double_adc_data = new double[25000];

            for (i = 0; i < 25000; i++)
            {
                double_adc_data[i] = ((double)adc_data[i] - 2048) / 2048;
            }


            ToneAnalysisResult toneAnalysisResult = HarmonicAnalyzer.ToneAnalysis(double_adc_data, 1, 10, true);

            label2.Text = "ENOB:" + toneAnalysisResult.ENOB;
            label3.Text="SINAD:"+ toneAnalysisResult.SINAD;
            label4.Text="SNR:"+ toneAnalysisResult.SNR;
            label5.Text="THD:"+ toneAnalysisResult.THD;

            easyChart_readData1.Plot(double_adc_data);*/
        }
    }
}
