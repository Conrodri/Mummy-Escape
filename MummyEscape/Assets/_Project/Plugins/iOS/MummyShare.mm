#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

// Native share sheet for Mummy Rush results (called from ShareService.cs).
extern "C" void _MummyShareText(const char* text)
{
    NSString* message = [NSString stringWithUTF8String:text];
    UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:@[message]
                                                                        applicationActivities:nil];
    UIViewController* root = UnityGetGLViewController();
    // iPad requires an anchor for the popover.
    sheet.popoverPresentationController.sourceView = root.view;
    sheet.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height, 1, 1);
    [root presentViewController:sheet animated:YES completion:nil];
}

// Device region (ISO 3166 alpha-2, e.g. "FR") for the per-country leaderboard (called from CountryService.cs).
// The returned buffer is freed by the IL2CPP marshaller.
extern "C" char* _MummyCountryCode()
{
    NSString* code = [[NSLocale currentLocale] objectForKey:NSLocaleCountryCode];
    const char* utf8 = code != nil ? [code UTF8String] : "";
    char* copy = (char*)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}
