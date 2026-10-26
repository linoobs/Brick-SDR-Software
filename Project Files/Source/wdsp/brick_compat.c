/*  brick_compat.c

Brick-SDR: entry points the Thetis code calls that WDSP 2.11 does not have.
They do nothing, so the features behind them (NR3/NR4, extra PureSignal
options, spectrum pixel reference) are not available with WDSP 2.11.
*/

#include "comm.h"

PORT void GetTXACFCOMPGainAndMask(int channel, double* gain_values, double* mask_values, int* ready) { if (ready) *ready = 0; }
PORT void SetPSIntsAndSpi(int channel, int ints, int spi) {}
PORT void SetPSMapMode(int channel, int map) {}
PORT void SetPSPinMode(int channel, int pin) {}
PORT void SetPSPtol(int channel, double ptol) {}
PORT void SetPSStabilize(int channel, int stbl) {}
PORT void SetPixelRef(int disp, double pixel_ref) {}
PORT void SetRXACBLPosition(int channel, int position) {}
PORT void psccF(int channel, int size, float* Itxbuff, float* Qtxbuff, float* Irxbuff, float* Qrxbuff, int mox, int solidmox) {}
