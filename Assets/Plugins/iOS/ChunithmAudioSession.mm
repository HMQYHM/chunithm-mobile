#import <AVFoundation/AVFoundation.h>
#include <string>

// Unity owns category, activation and output routing. Request a preferred I/O
// duration only; forcing speaker/category here would break Bluetooth and BGM.
extern "C" const char* ChuAudioSessionReport(double preferredSeconds) {
    static std::string report;
    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;
    BOOL accepted = [session setPreferredIOBufferDuration:preferredSeconds error:&error];
    NSMutableArray<NSString*>* routes = [NSMutableArray array];
    for (AVAudioSessionPortDescription* port in session.currentRoute.outputs) [routes addObject:port.portType];
    NSString* text = [NSString stringWithFormat:@"iOS preferredAccepted=%d, requested=%.3fms, actualIO=%.3fms, sampleRate=%.0fHz, outputLatency=%.3fms, route=%@, error=%@",
        accepted, preferredSeconds*1000, session.IOBufferDuration*1000, session.sampleRate,
        session.outputLatency*1000, [routes componentsJoinedByString:@","], error.localizedDescription ?: @""];
    report = text.UTF8String;
    return report.c_str();
}
