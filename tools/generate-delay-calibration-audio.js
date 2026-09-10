const fs = require('fs');
const path = require('path');
const vm = require('vm');

const root = path.resolve(__dirname, '..');
const lameContext = {};
vm.runInNewContext(fs.readFileSync(path.join(root, '.tools', 'delay-audio', 'node_modules',
  'lamejs', 'lame.all.js'), 'utf8'), lameContext);
const lame = lameContext.lamejs;
const ready = readPcmWave(path.join(root, 'Assets', 'Resources', 'Audio', 'delay_ready.wav'));
const answer = readPcmWave(path.join(root, 'Assets', 'Resources', 'Audio', 'se_touch_answer.wav'));
const sampleRate = 44100;
const durationSeconds = 30;
const left = new Float32Array(sampleRate * durationSeconds);
const right = new Float32Array(sampleRate * durationSeconds);

for (let cycle = 0; cycle < 10; cycle++) {
  const start = cycle * 3;
  mix(left, right, ready, start, sampleRate, 0.72);
  mix(left, right, ready, start + 1, sampleRate, 0.72);
  mix(left, right, answer, start + 2, sampleRate, 0.88);
}

const encoder = new lame.Mp3Encoder(2, sampleRate, 128);
const chunks = [];
const block = 1152;
for (let offset = 0; offset < left.length; offset += block) {
  const l = toInt16(left.subarray(offset, Math.min(offset + block, left.length)));
  const r = toInt16(right.subarray(offset, Math.min(offset + block, right.length)));
  const encoded = encoder.encodeBuffer(l, r);
  if (encoded.length) chunks.push(Buffer.from(encoded));
}
const tail = encoder.flush();
if (tail.length) chunks.push(Buffer.from(tail));
const output = path.join(root, 'Assets', 'StreamingAssets', 'DelayCalibration', 'audio', 'gameplay.mp3');
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, Buffer.concat(chunks));

function readPcmWave(file) {
  const data = fs.readFileSync(file);
  if (data.toString('ascii', 0, 4) !== 'RIFF' || data.toString('ascii', 8, 12) !== 'WAVE')
    throw new Error(`Not a RIFF wave: ${file}`);
  let offset = 12;
  let format;
  let pcm;
  while (offset + 8 <= data.length) {
    const id = data.toString('ascii', offset, offset + 4);
    const size = data.readUInt32LE(offset + 4);
    const body = offset + 8;
    if (id === 'fmt ') format = {
      type: data.readUInt16LE(body), channels: data.readUInt16LE(body + 2),
      rate: data.readUInt32LE(body + 4), bits: data.readUInt16LE(body + 14)
    };
    if (id === 'data') pcm = data.subarray(body, body + size);
    offset = body + size + (size & 1);
  }
  if (!format || !pcm || format.type !== 1 || format.bits !== 16)
    throw new Error(`Only 16-bit PCM is supported: ${file}`);
  const frames = Math.floor(pcm.length / (format.channels * 2));
  const channels = [new Float32Array(frames), new Float32Array(frames)];
  for (let frame = 0; frame < frames; frame++) {
    channels[0][frame] = pcm.readInt16LE((frame * format.channels) * 2) / 32768;
    channels[1][frame] = pcm.readInt16LE((frame * format.channels + Math.min(1, format.channels - 1)) * 2) / 32768;
  }
  return { rate: format.rate, channels };
}

function mix(targetLeft, targetRight, source, startSeconds, targetRate, gain) {
  const start = Math.round(startSeconds * targetRate);
  const outputFrames = Math.ceil(source.channels[0].length * targetRate / source.rate);
  for (let frame = 0; frame < outputFrames && start + frame < targetLeft.length; frame++) {
    const sourcePosition = frame * source.rate / targetRate;
    const first = Math.floor(sourcePosition);
    const second = Math.min(first + 1, source.channels[0].length - 1);
    const fraction = sourcePosition - first;
    targetLeft[start + frame] = clamp(targetLeft[start + frame] +
      (source.channels[0][first] * (1 - fraction) + source.channels[0][second] * fraction) * gain);
    targetRight[start + frame] = clamp(targetRight[start + frame] +
      (source.channels[1][first] * (1 - fraction) + source.channels[1][second] * fraction) * gain);
  }
}

function toInt16(values) {
  const output = new Int16Array(values.length);
  for (let index = 0; index < values.length; index++)
    output[index] = Math.round(clamp(values[index]) * (values[index] < 0 ? 32768 : 32767));
  return output;
}

function clamp(value) { return Math.max(-1, Math.min(1, value)); }
