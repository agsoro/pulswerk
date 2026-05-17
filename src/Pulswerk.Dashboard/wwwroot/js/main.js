import { DrawioCodec } from './dashboards/scada/drawio.codec';
import { ConditionEvaluator } from './dashboards/scada/condition.evaluator';
import { ScadaPickerController } from './dashboards/scada/scada.picker.controller';
import { ScadaAnimationController } from './dashboards/scada/scada.animation.controller';
// Export to global window scope for legacy JS interoperability
window.DrawioCodec = DrawioCodec;
window.ConditionEvaluator = ConditionEvaluator;
window.ScadaPickerController = ScadaPickerController;
window.ScadaAnimationController = ScadaAnimationController;
console.log('Pulswerk Modular TypeScript Ecosystem Initialized Successfully.');
