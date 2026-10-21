"""Create PUBLIC example inputs, never deployment keys. Requires cryptography.

RSA is freshly generated. Other examples use small/public fixed scalars or seeds.
Only public-domain curve constants and this repository's public domain parameters
are used. Do not point this script at real private keys.
"""
import json
from pathlib import Path
from cryptography.hazmat.primitives.asymmetric import ec, ed25519, ed448, rsa, x25519, x448
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat


def h(n, size=None):
    return n.to_bytes(size or max(1, (n.bit_length() + 7)//8), 'big').hex()


def main():
    assets = Path(__file__).resolve().parents[1] / 'assets'
    result = {}
    for bits, curve in [(192,ec.SECP192R1()), (224,ec.SECP224R1()), (256,ec.SECP256R1()), (384,ec.SECP384R1()), (521,ec.SECP521R1())]:
        local = ec.derive_private_key(1, curve).public_key().public_numbers()
        peer = ec.derive_private_key(2, curve).public_key().public_numbers()
        size = (bits+7)//8
        result['ecc'+str(bits)] = {'private': h(1,size), 'publicX': h(local.x,size), 'publicY': h(local.y,size), 'peerX': h(peer.x,size), 'peerY': h(peer.y,size)}
    n = rsa.generate_private_key(public_exponent=65537, key_size=2048).private_numbers()
    result['rsa2048'] = {'rsaN': h(n.public_numbers.n), 'rsaE': h(n.public_numbers.e), 'rsaD': h(n.d)}
    for name, factory, size in [('ed25519', ed25519.Ed25519PrivateKey,32), ('ed448',ed448.Ed448PrivateKey,57), ('x25519',x25519.X25519PrivateKey,32), ('x448',x448.X448PrivateKey,56)]:
        seed = bytes(range(size))
        local = factory.from_private_bytes(seed)
        peer = factory.from_private_bytes(bytes(range(1,size+1)))
        result[name] = {'private': seed.hex(), 'publicX': local.public_key().public_bytes(Encoding.Raw,PublicFormat.Raw).hex(), 'peerX':peer.public_key().public_bytes(Encoding.Raw,PublicFormat.Raw).hex()}
    for name, domain in json.loads((assets/'domains.json').read_text()).items():
        p,g = int(domain['p'],16),int(domain['g'],16)
        result[name] = {'private':h(7), 'publicX':h(pow(g,7,p)), 'peerX':h(pow(g,11,p))}
    p = int('FFFFFFFEFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF',16)
    a = p-3
    gx = int('32C4AE2C1F1981195F9904466A39C9948FE30BBFF2660BE1715A4589334C74C7',16)
    gy = int('BC3736A2F4F6779C59BDCEE36B692153D0A9877CC62A474002DF32E52139F0A0',16)
    def add(left,right):
        x,y=left; u,v=right
        slope=((3*x*x+a)*pow(2*y,-1,p) if left==right else (v-y)*pow(u-x,-1,p))%p
        xx=(slope*slope-x-u)%p
        return xx,(slope*(x-xx)-y)%p
    points=[(gx,gy)]
    for _ in range(3): points.append(add(points[-1],points[0]))
    result['sm2']={'private':h(1,32),'publicX':h(gx,32),'publicY':h(gy,32),
                   'ephemeralPrivate':h(2,32),'ephemeralX':h(points[1][0],32),'ephemeralY':h(points[1][1],32),
                   'peerX':h(points[2][0],32),'peerY':h(points[2][1],32),
                   'peerEphemeralX':h(points[3][0],32),'peerEphemeralY':h(points[3][1],32)}
    (assets/'example_keys.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(len(result),'public input sets written')


if __name__ == '__main__':
    main()
