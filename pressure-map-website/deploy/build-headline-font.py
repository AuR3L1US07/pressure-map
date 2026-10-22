from pathlib import Path
import sys, string
sys.path.insert(0, r'E:\AIGC\pressure-map-website\.tools\fontbuild')
from fontTools.ttLib import TTFont
from fontTools import subset
from fontTools.varLib.instancer import instantiateVariableFont
root=Path(r'E:\AIGC\pressure-map-website')
font=TTFont(r'C:\Windows\Fonts\NotoSerifSC-VF.ttf')
text='把忙碌的学期，变成心里有数的每一天。把零散的任务，变成井井有条的安排。把眼前的压力，变成从容有序的节奏。'+string.ascii_letters+string.digits+string.punctuation+' \u00a0'
options=subset.Options()
options.name_IDs=['*']
options.name_legacy=True
options.name_languages=['*']
subsetter=subset.Subsetter(options=options)
subsetter.populate(text=text)
subsetter.subset(font)
font=instantiateVariableFont(font, {'wght':600}, inplace=True)
for record in font['name'].names:
    names={1:'PressureMap Display',2:'SemiBold',4:'PressureMap Display SemiBold',6:'PressureMapDisplay-SemiBold',16:'PressureMap Display',17:'SemiBold'}
    if record.nameID in names:
        record.string=names[record.nameID].encode(record.getEncoding())
font.flavor='woff2'
output=root/'dist/assets/fonts/pressuremap-headline.woff2'
font.save(output)
print('Font subset:',output.stat().st_size,'bytes')
