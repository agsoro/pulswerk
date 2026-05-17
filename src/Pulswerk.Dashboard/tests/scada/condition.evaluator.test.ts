import { describe, it, expect } from 'vitest';
import { ConditionEvaluator } from '../../src/frontend/dashboards/scada/condition.evaluator';

describe('ConditionEvaluator', () => {
    it('evaluates greater than correctly', () => {
        expect(ConditionEvaluator.evaluate(50, '> 40')).toBe(true);
        expect(ConditionEvaluator.evaluate(30, '> 40')).toBe(false);
    });

    it('evaluates less than correctly', () => {
        expect(ConditionEvaluator.evaluate(20, '< 30')).toBe(true);
        expect(ConditionEvaluator.evaluate(40, '< 30')).toBe(false);
    });

    it('evaluates between range correctly', () => {
        expect(ConditionEvaluator.evaluate(50, 'between 20 80')).toBe(true);
        expect(ConditionEvaluator.evaluate(10, 'between 20 80')).toBe(false);
        expect(ConditionEvaluator.evaluate(90, 'between 20 80')).toBe(false);
    });

    it('evaluates equality correctly', () => {
        expect(ConditionEvaluator.evaluate(5, '== 5')).toBe(true);
        expect(ConditionEvaluator.evaluate(5, '= 5')).toBe(true);
        expect(ConditionEvaluator.evaluate(6, '== 5')).toBe(false);
    });

    it('handles empty or missing formulas gracefully', () => {
        expect(ConditionEvaluator.evaluate(100, '')).toBe(true);
        expect(ConditionEvaluator.evaluate(100, undefined)).toBe(true);
    });

    it('handles invalid numbers gracefully', () => {
        expect(ConditionEvaluator.evaluate('invalid', '> 10')).toBe(false);
    });
});
