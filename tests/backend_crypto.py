"""Generate and independently check SM2/HMAC-SM3 backend regression fixtures.

The generated signing fixtures add negative verification checks for testing only.
No generator templates or target firmware are changed by this script.
"""
import argparse
import hashlib
import hmac
import json
from pathlib import Path
import re
import subprocess
import uuid


P = int('FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF', 16)
A = P - 3
B = int('28E9FA9E9D9F5E344D5A9E4BCF6509A7F39789F515AB8F92DDBCBD414D940E93', 16)
N = int('FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFF7203DF6B21C6052B53BBF40939D54123', 16)
G = (int('32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7', 16),
     int('BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0', 16))


def add(p, q):
    if p is None:
        return q
    if q is None:
        return p
    x, y = p
    u, v = q
    if x == u and (y + v) % P == 0:
        return None
    slope = ((3*x*x + A) * pow(2*y, -1, P) if p == q else
             (v-y) * pow(u-x, -1, P)) % P
    z = (slope*slope-x-u) % P
    return z, (slope*(x-z)-y) % P


def mul(k, p=G):
    result = None
    while k:
        if k & 1:
            result = add(result, p)
        p = add(p, p)
        k >>= 1
    return result


def sm3(data):
    return hashlib.new('sm3', data).digest()


def octets(n):
    return n.to_bytes(32, 'big')


def za(identity, public):
    return sm3((len(identity)*8).to_bytes(2, 'big') + identity +
               b''.join(octets(v) for v in (A, B, *G, *public)))


def kep(options):
    d = int(options['private'], 16)
    r = int(options['ephemeralPrivate'], 16)
    peer = tuple(int(options[k], 16) for k in ('peerX', 'peerY'))
    peer_eph = tuple(int(options[k], 16) for k in ('peerEphemeralX', 'peerEphemeralY'))
    xbar = lambda x: (1 << 127) + (x & ((1 << 127)-1))
    t = (d + xbar(mul(r)[0])*r) % N
    point = mul(t, add(peer, mul(xbar(peer_eph[0]), peer_eph)))
    own_z = za(bytes.fromhex(options['identity']), mul(d))
    peer_z = za(bytes.fromhex(options['peerIdentity']), peer)
    z = own_z + peer_z if options['role'] == 'initiator' else peer_z + own_z
    data = octets(point[0]) + octets(point[1]) + z
    length = int(options['outputBytes'])
    return b''.join(sm3(data + i.to_bytes(4, 'big'))
                    for i in range(1, (length+31)//32+1))[:length]


def signature_valid(signature, options):
    if len(signature) != 64:
        return False
    r = int.from_bytes(signature[:32], 'big')
    s = int.from_bytes(signature[32:], 'big')
    if not (0 < r < N and 0 < s < N) or (r+s) % N == 0:
        return False
    public = tuple(int(options[k], 16) for k in ('publicX', 'publicY'))
    e = int.from_bytes(sm3(za(bytes.fromhex(options['identity']), public) +
                          bytes.fromhex(options['message'])), 'big')
    point = add(mul(s), mul((r+s) % N, public))
    return point is not None and (e+point[0]) % N == r


NEGATIVE_CHECKS = '''
    /* Regression fixture only: valid verification must not accept mutations. */
    {
        uint8_t invalid_signature[64];
        uint8_t invalid_hash[32];
        for (unsigned int mutation = 0; mutation < 4; ++mutation) {
            TEE_MemMove(invalid_signature, response->data, 64);
            TEE_MemMove(invalid_hash, input, 32);
            if (mutation == 0)
                invalid_signature[0] ^= 1;
            else if (mutation == 1)
                invalid_hash[0] ^= 1;
            else if (mutation == 2)
                TEE_MemFill(invalid_signature, 0, 32);
            else
                TEE_MemFill(invalid_signature + 32, 0, 32);
            result = TEE_AsymmetricVerifyDigest(verify, NULL, 0,
                        invalid_hash, sizeof(invalid_hash),
                        invalid_signature, sizeof(invalid_signature));
            if (result != TEE_ERROR_SIGNATURE_INVALID) {
                result = TEE_ERROR_GENERIC;
                goto cleanup;
            }
        }
        result = TEE_SUCCESS;
    }
'''


def generate(exe, out):
    out.mkdir(parents=True, exist_ok=False)
    features = []
    def feature(kind, algorithm, **options):
        features.append(dict(Kind=kind, Algorithm=algorithm,
                             Options={k: str(v) for k, v in options.items()}))
    # Every allowed HMAC-SM3 key size, not just the former failing boundaries.
    for bits in range(80, 1025, 8):
        feature('mac', 'TEE_ALG_HMAC_SM3', keySource='input',
                key=bytes(range(bits//8)).hex(), message='616263',
                streaming='true' if bits % 16 == 0 else 'false')
    for bits in (80, 504, 512, 520, 528, 1016, 1024):
        for streaming in ('false', 'true'):
            feature('mac', 'TEE_ALG_HMAC_SM3', keySource='generate',
                    keyBits=bits, streaming=streaming)
    # Both sides must agree and match an independent SM2/KDF calculation.
    for length in (1, 16, 32, 64, 65, 128, 255, 256, 511, 512):
        for role, d, eph, peer_d, peer_eph in (
                ('initiator', 1, 2, 3, 4), ('responder', 3, 4, 1, 2)):
            public, ephemeral, peer, remote = map(mul, (d, eph, peer_d, peer_eph))
            feature('derive', 'TEE_ALG_SM2_KEP', keySource='input', role=role,
                    private=octets(d).hex(), publicX=octets(public[0]).hex(),
                    publicY=octets(public[1]).hex(), ephemeralPrivate=octets(eph).hex(),
                    ephemeralX=octets(ephemeral[0]).hex(), ephemeralY=octets(ephemeral[1]).hex(),
                    peerX=octets(peer[0]).hex(), peerY=octets(peer[1]).hex(),
                    peerEphemeralX=octets(remote[0]).hex(), peerEphemeralY=octets(remote[1]).hex(),
                    identity=(b'Alice' if role == 'initiator' else b'Bob').hex(),
                    peerIdentity=(b'Bob' if role == 'initiator' else b'Alice').hex(),
                    outputBytes=length)
    for d in (1, 2, 3, 4, N-2):
        public = mul(d)
        for message in ('', '616263', bytes(range(128)).hex()):
            feature('sign', 'TEE_ALG_SM2_DSA_SM3', keySource='input',
                    private=octets(d).hex(), publicX=octets(public[0]).hex(),
                    publicY=octets(public[1]).hex(), identity=b'Alice'.hex(), message=message)
    for _ in range(4):
        feature('sign', 'TEE_ALG_SM2_DSA_SM3', keySource='generate')
    for bits, digest in ((192, 'SHA1'), (224, 'SHA224'), (256, 'SHA256'),
                         (384, 'SHA384'), (521, 'SHA512')):
        for source in ('input', 'generate'):
            feature('sign', 'TEE_ALG_ECDSA_'+digest, keySource=source,
                    curve='TEE_ECC_CURVE_NIST_P'+str(bits))
            feature('derive', 'TEE_ALG_ECDH_DERIVE_SHARED_SECRET', keySource=source,
                    curve='TEE_ECC_CURVE_NIST_P'+str(bits))
    for source in ('input', 'generate'):
        for message in ('', '616263', bytes(range(128)).hex()):
            feature('encrypt', 'TEE_ALG_SM2_PKE', keySource=source, message=message)
    projects = []
    negative_checks = 0
    for index, start in enumerate(range(0, len(features), 80), 1):
        name = 'crypto_fix_'+str(index)
        config = dict(FormatVersion=1, Name=name, Uuid=str(uuid.uuid4()),
                      Features=features[start:start+80])
        path = out/(name+'.json')
        path.write_text(json.dumps(config, indent=2), encoding='utf-8')
        subprocess.run([str(exe.resolve()), '--generate', str(path), str(out/name),
                        str(out/(name+'-report.json'))], check=True, timeout=180)
        for source in (out/name/'ta/examples').glob('*sm2_sign*.c'):
            text = source.read_text(encoding='utf-8')
            marker = '\tresponse->data_size = (uint32_t)signature_size;'
            assert text.count(marker) == 1
            source.write_text(text.replace(marker, NEGATIVE_CHECKS+marker), encoding='utf-8')
            negative_checks += 4
        projects.append(str(out/name))
    summary = dict(Cases=len(features), Projects=projects,
                   NegativeSignatureChecks=negative_checks)
    (out/'matrix.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def analyze(runs, output):
    checks = []
    failures = []
    targets = {"TEE_ALG_HMAC_SM3", "TEE_ALG_SM2_KEP", "TEE_ALG_SM2_DSA_SM3"}
    require(bool(runs), 'At least one runtime report is required.')
    for path in runs:
        report = json.loads(path.read_text(encoding='utf-8'))
        require(report.get('Completed') is True, f'{path}: run did not complete')
        require(bool(report.get('Cases')), f'{path}: run contains no cases')
        seen = set()
        for case in report['Cases']:
            case_id = case['Id']
            require(case_id not in seen, f'{path}: duplicate case {case_id}')
            seen.add(case_id)
            feature = case['Feature']
            options = feature.get('Options', {})
            targeted = feature.get('Algorithm') in targets
            if case['ExitCode'] != 0:
                failures.append(case)
                checks.append(dict(Case=case['Id'], Passed=False, Independent=False, Targeted=targeted))
                continue
            statuses = re.findall(
                r'result=(0x[0-9a-f]+) origin=\d+ value=\(\d+,\d+\) bytes=(\d+)',
                case['Output'])
            require(len(statuses) == 1 and int(statuses[0][0], 16) == 0,
                    f'{case_id}: exit status and GP result disagree')
            matches = re.findall(r'^data \((\d+) bytes\): ([0-9a-f]*)$',
                                 case['Output'], re.M)
            require(len(matches) <= 1, f'{case_id}: duplicate data output')
            data = bytes.fromhex(matches[0][1]) if matches else b''
            require(len(data) == int(statuses[0][1]),
                    f'{case_id}: result length disagrees with data')
            if matches:
                require(len(data) == int(matches[0][0]),
                        f'{case_id}: data header length disagrees with data')
            algorithm = feature.get('Algorithm')
            checked = False
            if algorithm == 'TEE_ALG_HMAC_SM3' and options.get('keySource') == 'input':
                expected = hmac.new(bytes.fromhex(options['key']), bytes.fromhex(options['message']), 'sm3').digest()
                require(data == expected, f'{case_id}: HMAC mismatch')
                checked = True
            elif algorithm == 'TEE_ALG_SM2_KEP':
                require(data == kep(options), f'{case_id}: KEP mismatch')
                checked = True
            elif algorithm == 'TEE_ALG_SM2_DSA_SM3' and options.get('keySource') == 'input':
                require(signature_valid(data, options), f'{case_id}: invalid signature')
                checked = True
            checks.append(dict(Case=case['Id'], Passed=True, Independent=checked, Targeted=targeted))
    targeted_checks = [c for c in checks if c['Targeted']]
    summary = dict(Passed=not failures, Cases=len(checks),
                   SuccessfulCases=sum(c["Passed"] for c in checks),
                   TargetedPassed=bool(targeted_checks) and all(c['Passed'] for c in targeted_checks),
                   TargetedCases=sum(c["Targeted"] for c in checks),
                   IndependentChecks=sum(c["Independent"] for c in checks),
                   Failures=failures, Checks=checks)
    output.write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in summary.items() if k not in ('Checks', 'Failures')}))
    raise SystemExit(0 if summary['Passed'] else 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--runs', type=Path, nargs='+')
    args = parser.parse_args()
    if args.runs:
        analyze(args.runs, args.output)
    else:
        if args.exe is None:
            parser.error('--exe is required for generation')
        generate(args.exe, args.output)
