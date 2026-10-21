"""Classify real QEMU outcomes and verify deterministic returned values."""
import argparse
import hashlib
import hmac
import json
from pathlib import Path
import re


def main():
    p=argparse.ArgumentParser()
    p.add_argument('--runs',nargs='+',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    results=[];observed=set();oracles=[];capabilities=[]
    catalog=json.loads((Path(__file__).resolve().parents[1]/'assets/catalog.json').read_text(encoding='utf-8-sig'))
    for run in args.runs:
        report=json.loads(run.read_text())
        assert report['Completed'],str(run)
        observed.update(report['ObservedApis'])
        for case in report['Cases']:
            feature=case['Feature'];kind=feature['Kind'];algorithm=feature.get('Algorithm','');options=feature.get('Options',{})
            status=re.search(r'result=(0x[0-9a-f]+) origin=(\d+) value=\((\d+),(\d+)\) bytes=(\d+)',case['Output'])
            code=int(status[1],16) if status else None
            data=re.search(r'^data \(\d+ bytes\): ([0-9a-f]*)$',case['Output'],re.M)
            data=bytes.fromhex(data[1]) if data else b''
            classification='success' if case['ExitCode']==0 and code==0 else 'unsupported' if case['ExitCode']==1 and code==0xffff000a else 'unexpected'
            row={'Run':str(run),'Id':case['Id'],'Command':case['Command'],'Feature':feature,'Status':classification,'Result':status[1] if status else None,'ExitCode':case['ExitCode']}
            if kind=='algorithm_support' and classification=='success':
                capabilities.append(dict(row,Supported=int(status[3])==1,QueryResult=hex(int(status[4]))))
                continue
            if classification=='success':
                assert int(status[5])==len(data),(case['Id'],'output length')
                expected=None
                if kind=='digest':
                    name=algorithm.removeprefix('TEE_ALG_').lower().replace('shake','shake_')
                    message=bytes.fromhex(options.get('message','616263'))
                    expected=hashlib.new(name,message)
                    expected=expected.digest(int(options.get('outputBytes','32'))) if 'shake' in name else expected.digest()
                elif kind=='mac' and algorithm.startswith('TEE_ALG_HMAC_') and options.get('keySource')=='input':
                    name=algorithm.removeprefix('TEE_ALG_HMAC_').lower()
                    key=bytes.fromhex(options.get('key',bytes(range(32)).hex()))
                    expected=hmac.new(key,bytes.fromhex(options.get('message','616263')),name).digest()
                elif kind=='bigint':
                    expected=b'\x11';assert int(status[3])==17
                elif kind=='fmm':assert int(status[3])==12
                elif kind=='random':assert len(data)==int(options.get('bytes','32')) and any(data)
                elif kind=='client_cancel':assert int(status[3])==1 and int(status[4])==0xffff0002,(case['Id'],'cancellation did not arrive')
                elif kind=='property' and options.get('name')=='gp.runtime.u64':assert int(status[3])==0x9abcdef0 and int(status[4])==0x12345678
                elif kind=='property' and options.get('name')=='gp.runtime.binary':expected=b'abc'
                if expected is not None:
                    assert data==expected,(case['Id'],algorithm,data.hex(),expected.hex())
                    oracles.append(case['Id'])
            results.append(row)
    expected={a['Name'] for a in catalog['Apis'] if a['Name']!='TEE_BigIntSizeInU32'}
    unexpected=[r for r in results if r['Status']=='unexpected']
    missing=sorted(expected-observed)
    summary={'AllRunsCompleted':True,'ExecutionCases':len(results),'SuccessfulCases':sum(r['Status']=='success' for r in results),
             'UnsupportedCases':sum(r['Status']=='unsupported' for r in results),'UnexpectedCases':unexpected,
             'CapabilityQueries':len(capabilities),'ObservedRuntimeApis':len(expected&observed),'ExpectedRuntimeApis':len(expected),
             'MissingApis':missing,'OracleChecks':len(oracles),'Oracles':oracles,'Cases':results,'Capabilities':capabilities,
             'Accepted':not unexpected and not missing,'Scope':'Runtime function coverage; unsupported combinations are not passes or conformance certification.'}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k not in ('Cases','Capabilities','Oracles')},indent=2))
    raise SystemExit(0 if summary['Accepted'] else 1)

if __name__=='__main__':main()
