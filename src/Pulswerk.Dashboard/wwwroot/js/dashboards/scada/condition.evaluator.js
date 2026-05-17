export class ConditionEvaluator {
    static evaluate(values, condition) {
        if (!condition || condition.trim() === '')
            return true;
        const isLegacyFormula = /^(between|<|>|<=|>=|==|=|!=)\s+-?\d/.test(condition.trim());
        const firstVal = Array.isArray(values) ? values[0] : values;
        if (isLegacyFormula) {
            const num = parseFloat(firstVal);
            if (isNaN(num))
                return false;
            const parts = condition.trim().split(/\s+/);
            const op = parts[0];
            const operand = parseFloat(parts[1]);
            if (isNaN(operand))
                return false;
            switch (op) {
                case 'between': return num >= operand && num <= parseFloat(parts[2]);
                case '<': return num < operand;
                case '>': return num > operand;
                case '<=': return num <= operand;
                case '>=': return num >= operand;
                case '==':
                case '=': return num === operand;
                case '!=': return num !== operand;
                default: return false;
            }
        }
        try {
            const vals = Array.isArray(values) ? values : [values];
            const argNames = vals.map((_, i) => `v${i}`);
            const fn = new Function(...argNames, `return (${condition});`);
            return !!fn(...vals.map(v => parseFloat(v) || 0));
        }
        catch (e) {
            console.warn("Error evaluating animation rule formula:", condition, e);
            return false;
        }
    }
}
