"""Check that malformed or incomplete reports cannot become successful results."""
import contextlib
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest

import backend_crypto


class AnalyzerContract(unittest.TestCase):
    def setUp(self):
        self.case = {
            'Id': 'sample-1',
            'ExitCode': 0,
            'Feature': {'Kind': 'random', 'Options': {}},
            'Output': 'random: result=0x00000000 origin=4 value=(0,0) bytes=1\n'
                      'data (1 bytes): ab',
        }

    def analyze(self, cases, completed=True):
        with tempfile.TemporaryDirectory(prefix='gp-analyzer-') as directory:
            root = Path(directory)
            report = root/'runtime.json'
            output = root/'analysis.json'
            report.write_text(json.dumps({'Completed': completed, 'Cases': cases}),
                              encoding='utf-8')
            with contextlib.redirect_stdout(io.StringIO()):
                try:
                    backend_crypto.analyze([report], output)
                except SystemExit as result:
                    return result.code, json.loads(output.read_text(encoding='utf-8'))
            self.fail('Analyzer did not return a status')

    def test_success_without_target_cases_does_not_claim_targeted_pass(self):
        code, result = self.analyze([self.case])
        self.assertEqual(code, 0)
        self.assertTrue(result['Passed'])
        self.assertFalse(result['TargetedPassed'])

    def test_empty_report_is_rejected(self):
        with self.assertRaises(ValueError):
            self.analyze([])

    def test_incomplete_report_is_rejected(self):
        with self.assertRaises(ValueError):
            self.analyze([self.case], completed=False)

    def test_duplicate_case_is_rejected(self):
        with self.assertRaises(ValueError):
            self.analyze([self.case, self.case])

    def test_inconsistent_success_output_is_rejected(self):
        outputs = [
            '',
            self.case['Output'].replace('0x00000000', '0xffff0006'),
            self.case['Output'].replace('bytes=1', 'bytes=2'),
            self.case['Output'].replace('(1 bytes)', '(2 bytes)'),
            self.case['Output'] + '\ndata (1 bytes): cd',
        ]
        for output in outputs:
            with self.subTest(output=output), self.assertRaises(ValueError):
                case = dict(self.case, Output=output)
                self.analyze([case])

    def test_failure_is_retained(self):
        case = copy.deepcopy(self.case)
        case['ExitCode'] = 1
        case['Feature'] = {'Kind': 'derive', 'Algorithm': 'TEE_ALG_SM2_KEP'}
        code, result = self.analyze([case])
        self.assertEqual(code, 1)
        self.assertFalse(result['Passed'])
        self.assertFalse(result['TargetedPassed'])
        self.assertEqual(result['Failures'][0]['Id'], case['Id'])

    def test_incorrect_hmac_is_rejected(self):
        case = copy.deepcopy(self.case)
        case['Feature'] = {
            'Kind': 'mac', 'Algorithm': 'TEE_ALG_HMAC_SM3',
            'Options': {'keySource': 'input', 'key': '01'*10, 'message': '616263'},
        }
        with self.assertRaises(ValueError):
            self.analyze([case])


if __name__ == '__main__':
    unittest.main()
