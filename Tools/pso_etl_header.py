"""Read finalized local ETL metadata using OpenTraceW, never start a session.

Win64 layout from Microsoft's EVENT_TRACE_LOGFILEW / TRACE_LOGFILE_HEADER.
Cross-check against tracerpt on the historical ETL before applying to new files.
"""
import ctypes as c
import json
import sys
from datetime import datetime,timedelta,timezone
from pathlib import Path

U32=c.c_uint32
I64=c.c_int64
class SYSTEMTIME(c.Structure):
    _fields_=[(name,c.c_uint16) for name in ('year','month','dayOfWeek','day','hour','minute','second','milliseconds')]
class TIMEZONE(c.Structure):
    _fields_=[('bias',c.c_int32),('standardName',c.c_wchar*32),('standardDate',SYSTEMTIME),('standardBias',c.c_int32),
              ('daylightName',c.c_wchar*32),('daylightDate',SYSTEMTIME),('daylightBias',c.c_int32)]
class HEADER(c.Structure):
    _fields_=[('bufferSize',U32),('version',U32),('providerVersion',U32),('numberOfProcessors',U32),('endTime',I64),
              ('timerResolution',U32),('maximumFileSize',U32),('logFileMode',U32),('buffersWritten',U32),
              ('startBuffers',U32),('pointerSize',U32),('eventsLost',U32),('cpuSpeedMHz',U32),
              ('loggerName',c.c_void_p),('logFileName',c.c_void_p),('timeZone',TIMEZONE),
              ('bootTime',I64),('perfFreq',I64),('startTime',I64),('reservedFlags',U32),('buffersLost',U32)]
class LOGFILE(c.Structure):
    _fields_=[('logFileName',c.c_wchar_p),('loggerName',c.c_wchar_p),('currentTime',I64),('buffersRead',U32),('processTraceMode',U32),
              ('currentEvent',c.c_uint64*11),('header',HEADER),('bufferCallback',c.c_void_p),
              ('bufferSize',U32),('filled',U32),('unusedEventsLost',U32),('eventCallback',c.c_void_p),('isKernelTrace',U32),('context',c.c_void_p)]

assert c.sizeof(c.c_void_p)==8 and c.sizeof(TIMEZONE)==172
assert c.sizeof(HEADER)==280 and c.sizeof(LOGFILE)==448 and LOGFILE.header.offset==120
api=c.WinDLL('advapi32',use_last_error=True)
api.OpenTraceW.argtypes=[c.POINTER(LOGFILE)]
api.OpenTraceW.restype=c.c_uint64
api.CloseTrace.argtypes=[c.c_uint64]
api.CloseTrace.restype=U32
callback=c.WINFUNCTYPE(None,c.c_void_p)(lambda event:None)

def read(path):
    log=LOGFILE()
    log.logFileName=str(Path(path).resolve())
    log.eventCallback=c.cast(callback,c.c_void_p)
    handle=api.OpenTraceW(c.byref(log))
    if handle==2**64-1: raise c.WinError(c.get_last_error())
    try:
        h=log.header
        if h.pointerSize!=8 or h.perfFreq<=0 or h.endTime<=h.startTime or h.providerVersion<10000:
            raise ValueError('Unsupported or unfinalized header; do not infer loss values')
        def stamp(ticks):
            return (datetime(1601,1,1,tzinfo=timezone.utc)+timedelta(microseconds=ticks//10)).isoformat()
        return dict(path=log.logFileName,bytes=Path(path).stat().st_size,method='OpenTraceW finalized TRACE_LOGFILE_HEADER; no ProcessTrace',
                    startFileTime=h.startTime,endFileTime=h.endTime,startUtc=stamp(h.startTime),endUtc=stamp(h.endTime),
                    durationSeconds=(h.endTime-h.startTime)/1e7,perfFreq=h.perfFreq,eventsLost=h.eventsLost,buffersLost=h.buffersLost,
                    buffersWritten=h.buffersWritten,pointerSize=h.pointerSize,providerVersion=h.providerVersion,
                    numberOfProcessors=h.numberOfProcessors,clockType=h.reservedFlags,timeZoneBiasMinutes=h.timeZone.bias,
                    limitation='Header counters only; no event decode or provider completeness claim')
    finally:
        error=api.CloseTrace(handle)
        if error: raise OSError(error,'CloseTrace failed')

if __name__=='__main__':
    result=[read(path) for path in sys.argv[1:]]
    print(json.dumps(result,indent=2))
